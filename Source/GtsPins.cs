using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GTS
{
	// Female zombie vs tiny player. An encounter opens with a random move (stomp with either foot, a hovering
	// slam, or a kick). A stomp that lands pins the victim: they can't move, the zombie keeps going through random
	// grinding moves, every foot landing costs health, mashing jump breaks free, and the landing that would kill
	// squashes them with the usual gore. A kick that lands throws the victim away instead.
	// Server side: picks moves, times the impacts (GtsMoves), deals damage, counts struggles.
	public static class GtsPins
	{
		private class Encounter
		{
			public EntityZombie zombie;
			public EntityPlayer victim;
			public GtsMove move;
			public float start;
			public int nextImpact;
			public bool pinned;
			public float struggle;
			public bool preview;   // gts movetest: one move, no damage
		}

		private static readonly Dictionary<int, Encounter> byZombie = new Dictionary<int, Encounter>();
		private static readonly Dictionary<int, Encounter> pinnedByVictim = new Dictionary<int, Encounter>();
		private static readonly Dictionary<int, float> cooldownUntil = new Dictionary<int, float>();
		private static readonly List<Encounter> snapshot = new List<Encounter>();

		// Any encounter in progress: the zombie is busy with its feet and shouldn't swing its arms or walk off.
		public static bool IsBusy(int _zombieId)
		{
			return byZombie.ContainsKey(_zombieId);
		}

		public static void Reset()
		{
			byZombie.Clear();
			pinnedByVictim.Clear();
			cooldownUntil.Clear();
		}

		// A female zombie got close to a tiny player who opted in.
		public static void TryEngage(EntityZombie _zombie, EntityPlayer _victim)
		{
			int id = _zombie.entityId;
			if (byZombie.ContainsKey(id) || pinnedByVictim.ContainsKey(_victim.entityId)
				|| (cooldownUntil.TryGetValue(id, out float until) && Time.time < until))
			{
				return;
			}
			Encounter e = new Encounter { zombie = _zombie, victim = _victim };
			byZombie[id] = e;
			StartMove(e, GtsMoves.PickOpener());
		}

		// gts movetest on the server / in single player: play one move with the real positioning, but no damage.
		public static void Preview(EntityZombie _zombie, EntityPlayer _victim, GtsMove _move)
		{
			if (byZombie.TryGetValue(_zombie.entityId, out Encounter old))
			{
				Finish(old, 0f);
			}
			Encounter e = new Encounter { zombie = _zombie, victim = _victim, preview = true };
			byZombie[_zombie.entityId] = e;
			StartMove(e, _move);
		}

		public static void Update(World _world, float _dt)
		{
			if (byZombie.Count == 0)
			{
				return;
			}
			snapshot.Clear();
			snapshot.AddRange(byZombie.Values);
			foreach (Encounter e in snapshot)
			{
				EntityZombie z = e.zombie;
				EntityPlayer v = e.victim;
				if (z == null || z.IsDead() || v == null || v.IsDead() || (!e.preview && !GtsConfig.IsShrunk(v)) || (e.pinned && TooFar(e)))
				{
					Finish(e, GtsConfig.StompCooldown);
					continue;
				}
				Steer(e, _dt);
				if (e.preview)
				{
					float[] marks = GtsMoves.Impacts(e.move);
					while (e.nextImpact < marks.Length && Time.time - e.start >= marks[e.nextImpact])
					{
						e.nextImpact++;
						Broadcast(e, NetPackageGtsPin.Splash);
					}
					if (Time.time - e.start >= GtsMoves.Length(e.move))
					{
						Finish(e, GtsConfig.StompCooldown);
					}
					continue;
				}
				if (e.pinned)
				{
					e.struggle = Mathf.Max(0f, e.struggle - GtsConfig.StruggleDecay * _dt);
				}

				float t = Time.time - e.start;
				float[] impacts = GtsMoves.Impacts(e.move);
				bool over = false;
				while (!over && e.nextImpact < impacts.Length && t >= impacts[e.nextImpact])
				{
					e.nextImpact++;
					over = Impact(_world, e);
				}
				if (over || !byZombie.ContainsKey(z.entityId))
				{
					continue;
				}
				if (t >= GtsMoves.Length(e.move))
				{
					if (e.pinned)
					{
						StartMove(e, GtsMoves.PickPinMove(e.move));
					}
					else
					{
						Finish(e, GtsConfig.StompCooldown);   // the opener missed, or it was a kick
					}
				}
			}
		}

		// The pinned victim mashed jump once.
		public static void Struggle(int _victimId)
		{
			if (!pinnedByVictim.TryGetValue(_victimId, out Encounter e))
			{
				return;
			}
			e.struggle += 1f;
			if (e.struggle >= GtsConfig.StruggleNeeded)
			{
				Log.Out("[GTS] {0} broke free from {1}", e.victim.GetDebugName(), e.zombie.GetDebugName());
				Finish(e, GtsConfig.BreakFreeCooldown);
			}
		}

		private static void StartMove(Encounter _e, GtsMove _move)
		{
			_e.move = _move;
			_e.start = Time.time;
			_e.nextImpact = 0;
			SingletonMonoBehaviour<ConnectionManager>.Instance.SendPackage(
				NetPackageManager.GetPackage<NetPackageGtsMove>().Setup(_e.zombie.entityId, _e.victim.entityId, _move));
			if (!GameManager.IsDedicatedServer)
			{
				GtsMoveAnim.Play(_e.zombie, _e.victim, _move);
			}
		}

		// Keep facing the victim and stand at the distance the move's foot positions were made for: the victim
		// StompFootReach in front (KickStandDistance for a kick), or for StandOn right over them.
		private static void Steer(Encounter _e, float _dt)
		{
			EntityZombie z = _e.zombie;
			Vector3 d = _e.victim.position - z.position;
			d.y = 0f;
			if (d.magnitude > 0.25f)
			{
				GtsDriver.FaceTarget(z, _e.victim);
			}
			else if (z.moveHelper != null)
			{
				z.moveHelper.Stop();
			}
			Vector3 fwd = z.GetForwardVector();
			fwd.y = 0f;
			fwd = fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
			float stand = _e.move == GtsMove.StandOn ? GtsConfig.StandOnBack
				: _e.move == GtsMove.Kick ? GtsConfig.KickStandDistance
				: GtsConfig.StompFootReach;
			float speed = _e.move == GtsMove.StandOn ? GtsConfig.StandOnStepSpeed : GtsConfig.MoveStepSpeed;
			Vector3 spot = _e.victim.position - fwd * stand;
			spot.y = z.position.y;
			if ((spot - z.position).magnitude > 0.03f)
			{
				z.SetPosition(Vector3.MoveTowards(z.position, spot, speed * _dt));
			}
		}

		// A foot lands. Returns true when the encounter is over.
		private static bool Impact(World _world, Encounter _e)
		{
			EntityZombie z = _e.zombie;
			EntityPlayer v = _e.victim;
			if (_e.move == GtsMove.Kick)
			{
				Vector3 d = v.position - z.position;
				d.y = 0f;
				Vector3 fwd = z.GetForwardVector();
				fwd.y = 0f;
				if (d.magnitude <= GtsConfig.KickRange && Vector3.Dot(d, fwd) > 0f)
				{
					if (Hurt(_world, _e, GtsConfig.KickDamage))
					{
						return true;
					}
					Broadcast(_e, NetPackageGtsPin.Kick);
				}
				return false;
			}

			if (!_e.pinned)
			{
				// an opener stomp: only counts if the victim is under the landing foot
				if (!UnderFoot(_e))
				{
					return false;
				}
				if (Hurt(_world, _e, GtsMoves.Damage(_e.move)))
				{
					return true;
				}
				_e.pinned = true;
				pinnedByVictim[v.entityId] = _e;
				Log.Out("[GTS] {0} pinned {1}", z.GetDebugName(), v.GetDebugName());
				Broadcast(_e, NetPackageGtsPin.Start);
				return false;
			}

			if (Hurt(_world, _e, GtsMoves.Damage(_e.move)))
			{
				return true;
			}
			Broadcast(_e, NetPackageGtsPin.Impact);
			return false;
		}

		private static bool UnderFoot(Encounter _e)
		{
			EntityZombie z = _e.zombie;
			EntityPlayer v = _e.victim;
			Vector3 fwd = z.GetForwardVector();
			fwd.y = 0f;
			fwd = fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
			Vector3 right = Vector3.Cross(Vector3.up, fwd);
			Vector3 foot = z.position + fwd * GtsConfig.StompFootReach + right * (GtsMoves.Side(_e.move) * 0.1f);
			float scale = GtsConfig.GetScale(v);
			float hitRadius = GtsConfig.StompFootRadius + GtsConfig.PlayerRadius * scale + GtsConfig.StompHitLeniency;
			Vector3 d = v.position - foot;
			return Mathf.Abs(d.y) <= GtsConfig.PlayerHeight * scale + GtsConfig.StompHeadroom && d.x * d.x + d.z * d.z <= hitRadius * hitRadius;
		}

		// Deal damage; the hit that would kill squashes instead. Returns true when the victim was squashed.
		private static bool Hurt(World _world, Encounter _e, int _damage)
		{
			if (_e.victim.Health <= _damage)
			{
				EntityZombie z = _e.zombie;
				EntityPlayer v = _e.victim;
				Finish(_e, GtsConfig.StompCooldown);
				GtsDriver.Instance?.Squash(_world, v, z);
				return true;
			}
			_e.victim.DamageEntity(new DamageSource(EnumDamageSource.External, EnumDamageTypes.Crushing, Vector3.down), _damage, false, 0f);
			return false;
		}

		private static bool TooFar(Encounter _e)
		{
			Vector3 d = _e.victim.position - _e.zombie.position;
			d.y = 0f;
			return d.magnitude > GtsConfig.PinBreakDistance;
		}

		private static void Finish(Encounter _e, float _cooldown)
		{
			int id = _e.zombie != null ? _e.zombie.entityId : -1;
			byZombie.Remove(id);
			cooldownUntil[id] = Time.time + _cooldown;
			if (_e.pinned)
			{
				_e.pinned = false;
				if (_e.victim != null)
				{
					pinnedByVictim.Remove(_e.victim.entityId);
				}
				Broadcast(_e, NetPackageGtsPin.End);
			}
		}

		private static void Broadcast(Encounter _e, byte _state)
		{
			int zid = _e.zombie != null ? _e.zombie.entityId : -1;
			int vid = _e.victim != null ? _e.victim.entityId : -1;
			SingletonMonoBehaviour<ConnectionManager>.Instance.SendPackage(NetPackageManager.GetPackage<NetPackageGtsPin>().Setup(zid, vid, _state));
			if (!GameManager.IsDedicatedServer)
			{
				GtsPinFx.Apply(zid, vid, _state);
			}
		}
	}

	// Client side of an encounter: the victim pressed flatter with each landing, blood and crunch, a kick throws
	// the local victim, and while pinned the local victim can't move or jump; jump presses go to the server.
	public static class GtsPinFx
	{
		private static readonly Dictionary<int, int> impacts = new Dictionary<int, int>();   // victimId -> landings so far

		public static bool LocalPinned { get; private set; }

		public static void Reset()
		{
			impacts.Clear();
			LocalPinned = false;
			GtsDownPose.Reset();
		}

		public static float Flatten(int _victimId)
		{
			if (!impacts.TryGetValue(_victimId, out int n))
			{
				return 1f;
			}
			return Mathf.Max(GtsConfig.GrindMinFlatten, 1f - GtsConfig.GrindFlattenFirst - n * GtsConfig.GrindFlattenStep);
		}

		public static void Apply(int _zombieId, int _victimId, byte _state)
		{
			World world = GameManager.Instance != null ? GameManager.Instance.World : null;
			if (world == null)
			{
				return;
			}
			EntityAlive zombie = world.GetEntity(_zombieId) as EntityAlive;
			EntityAlive victim = world.GetEntity(_victimId) as EntityAlive;
			EntityPlayerLocal local = world.GetPrimaryPlayer();
			EntityPlayerLocal localVictim = local != null && local.entityId == _victimId ? local : null;
			switch (_state)
			{
				case NetPackageGtsPin.Start:
					impacts[_victimId] = 0;
					GtsMoveAnim.SetHolding(zombie, true);
					GtsDownPose.Pin(victim, zombie);
					if (localVictim != null)
					{
						LocalPinned = true;
						GameManager.ShowTooltip(localVictim, Localization.Get("gtsPinnedTip"), _showImmediately: true);
					}
					Crunch(victim, localVictim, true);
					break;
				case NetPackageGtsPin.Impact:
					impacts[_victimId] = impacts.TryGetValue(_victimId, out int n) ? n + 1 : 1;
					GtsDownPose.Pin(victim, zombie);   // in case this client missed the start
					GtsDownPose.Impact(_victimId);
					Crunch(victim, localVictim, false);
					break;
				case NetPackageGtsPin.Splash:
					Crunch(victim, localVictim, false);
					break;
				case NetPackageGtsPin.End:
					impacts.Remove(_victimId);
					GtsMoveAnim.SetHolding(zombie, false);
					GtsDownPose.Release(_victimId);
					if (localVictim != null)
					{
						LocalPinned = false;
					}
					break;
				case NetPackageGtsPin.Kick:
					Crunch(victim, localVictim, true);
					if (localVictim != null && zombie != null && localVictim.vp_FPController != null)
					{
						Vector3 dir = localVictim.position - zombie.position;
						dir.y = 0f;
						dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : zombie.GetForwardVector();
						localVictim.vp_FPController.AddForce(dir * GtsConfig.KickForce + Vector3.up * GtsConfig.KickUp);
					}
					break;
			}
		}

		private static void Crunch(EntityAlive _victim, EntityPlayerLocal _localVictim, bool _heavy)
		{
			if (_victim == null)
			{
				return;
			}
			float scale = GtsConfig.HasShrinkBuff(_victim) ? GtsConfig.GetScale(_victim) : 1f;
			GtsGore.Squirt(_victim.position, scale, _heavy);
			Log.Out("[GTS] squirt under {0} at {1:F2} (ground {2:F2}), scale {3:0.00}", _victim.GetDebugName(), _victim.position,
				GtsGround.Find(_victim.position, out _), scale);
			Audio.Manager.Play(_victim.position, GtsConfig.GrindSound);
			if (_localVictim != null)
			{
				// only in first person with the camera on the player (not third person or the detached P camera)
				if (GtsConfig.GrindScreenBlood && _localVictim.cameraTransform != null && _localVictim.bFirstPersonView
					&& _localVictim.IsCameraAttachedToPlayerOrScope())
				{
					GtsGore.ScreenSpurt(_localVictim.cameraTransform);
				}
				GameManager.Instance.StartCoroutine(_localVictim.shakeCamera(Random.onUnitSphere, 0.25f, GtsConfig.GrindShake));
			}
		}

		// Called every frame on clients: jump presses while pinned become struggles.
		public static void Update(EntityPlayerLocal _player)
		{
			if (!LocalPinned || _player == null || _player.playerInput == null || !_player.playerInput.Jump.WasPressed)
			{
				return;
			}
			if (SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
			{
				GtsPins.Struggle(_player.entityId);
			}
			else
			{
				SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(NetPackageManager.GetPackage<NetPackageGtsStruggle>());
			}
		}
	}

	// Server -> clients: pinned / a foot landed / released / kicked.
	public class NetPackageGtsPin : NetPackage
	{
		public const byte Start = 0;
		public const byte Impact = 1;
		public const byte End = 2;
		public const byte Kick = 3;
		public const byte Splash = 4;   // gts movetest: a foot landed, effects only

		private int zombieId;
		private int victimId;
		private byte state;

		public override NetPackageDirection PackageDirection => NetPackageDirection.ToClient;

		public NetPackageGtsPin Setup(int _zombieId, int _victimId, byte _state)
		{
			zombieId = _zombieId;
			victimId = _victimId;
			state = _state;
			return this;
		}

		public override void read(PooledBinaryReader _br)
		{
			zombieId = _br.ReadInt32();
			victimId = _br.ReadInt32();
			state = _br.ReadByte();
		}

		public override void write(PooledBinaryWriter _bw)
		{
			base.write(_bw);
			_bw.Write(zombieId);
			_bw.Write(victimId);
			_bw.Write(state);
		}

		public override void ProcessPackage(World _world, GameManager _callbacks)
		{
			if (_world != null)
			{
				GtsPinFx.Apply(zombieId, victimId, state);
			}
		}

		public override int GetLength()
		{
			return 9;
		}
	}

	// Client -> server: the pinned player pressed jump.
	public class NetPackageGtsStruggle : NetPackage
	{
		public override NetPackageDirection PackageDirection => NetPackageDirection.ToServer;

		public override void read(PooledBinaryReader _br)
		{
		}

		public override void ProcessPackage(World _world, GameManager _callbacks)
		{
			if (_world != null && Sender != null)
			{
				GtsPins.Struggle(Sender.entityId);
			}
		}

		public override int GetLength()
		{
			return 0;
		}
	}

	// A pinned player can't jump (jump is the struggle key) ...
	[HarmonyPatch(typeof(vp_FPController), nameof(vp_FPController.CanStart_Jump))]
	public static class PinnedJumpPatch
	{
		public static void Postfix(ref bool __result)
		{
			if (GtsPinFx.LocalPinned)
			{
				__result = false;
			}
		}
	}

	// ... and a zombie busy with its feet doesn't also swing its arms.
	[HarmonyPatch(typeof(EAIApproachAndAttackTarget), nameof(EAIApproachAndAttackTarget.CanExecute))]
	public static class BusyAttackPatch
	{
		public static void Postfix(EAIApproachAndAttackTarget __instance, ref bool __result)
		{
			if (__result && GtsPins.IsBusy(__instance.theEntity.entityId))
			{
				__result = false;
			}
		}
	}

	[HarmonyPatch(typeof(EAIApproachAndAttackTarget), nameof(EAIApproachAndAttackTarget.Continue))]
	public static class BusyAttackContinuePatch
	{
		public static void Postfix(EAIApproachAndAttackTarget __instance, ref bool __result)
		{
			if (__result && GtsPins.IsBusy(__instance.theEntity.entityId))
			{
				__result = false;
			}
		}
	}
}
