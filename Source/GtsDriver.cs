using System.Collections.Generic;
using UnityEngine;

namespace GTS
{
	// Runs on every machine.
	//  - Client: keeps every player's model at the right visual scale, hides squashed bodies.
	//  - Server: detects stomps, kills the victim, spills blood blocks and cleans them up later.
	public class GtsDriver : MonoBehaviour
	{
		// For coroutines started from static code (Harmony patches).
		public static GtsDriver Instance { get; private set; }

		private struct BloodBlock
		{
			public Vector3i pos;
			public int type;
			public float removeAt;
		}

		// Players squashed recently -> time of the squash. While they are dead their body stays hidden, the death
		// camera is ours and zombies leave the corpse alone. Kept until a little after they are alive again, since
		// on a client the squash message can arrive a moment before the death itself.
		public static readonly Dictionary<int, float> Squashed = new Dictionary<int, float>();
		private const float SquashForgetDelay = 2f;

		// Model scale we last applied per entity, so we only touch models we changed ourselves.
		private readonly Dictionary<int, float> appliedScale = new Dictionary<int, float>();

		private readonly Dictionary<int, Vector3> lastPos = new Dictionary<int, Vector3>();
		private readonly List<BloodBlock> bloodBlocks = new List<BloodBlock>();
		private readonly List<EntityAlive> stompers = new List<EntityAlive>();

		private class PendingStomp
		{
			public EntityZombie zombie;
			public EntityPlayer victim;
			public float impactAt;
		}
		private readonly Dictionary<int, PendingStomp> pendingStomps = new Dictionary<int, PendingStomp>();
		private readonly Dictionary<int, float> stompCooldownUntil = new Dictionary<int, float>();
		private readonly List<int> doneStomps = new List<int>();
		private float lastCheck;

		private void Awake()
		{
			Instance = this;
		}

		private static World GetWorld()
		{
			return GameManager.Instance != null ? GameManager.Instance.World : null;
		}

		private void Update()
		{
			World world = GetWorld();
			if (world == null || !SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
			{
				return;
			}
			float dt = Time.time - lastCheck;
			if (dt >= GtsConfig.CheckInterval)
			{
				lastCheck = Time.time;
				CheckStomps(world, dt);
			}
			ResolveStomps(world);
			CleanupBlood(world);
		}

		// Runs on the dedicated server too: zombies are moved there, so a tiny player's colliders must be small
		// on the server as well, or zombies bump into a full-size invisible pillar instead of walking over them.
		private void LateUpdate()
		{
			World world = GetWorld();
			if (world == null)
			{
				appliedScale.Clear();
				Squashed.Clear();
				GtsColliders.Reset();
				return;
			}
			List<EntityPlayer> players = world.Players.list;
			for (int i = 0; i < players.Count; i++)
			{
				EntityPlayer p = players[i];
				if (p == null)
				{
					continue;
				}
				if (Squashed.TryGetValue(p.entityId, out float squashedAt) && !p.IsDead() && Time.time - squashedAt > SquashForgetDelay)
				{
					Squashed.Remove(p.entityId);
				}
				if (!GameManager.IsDedicatedServer)
				{
					ApplyVisualScale(p);
				}
				GtsColliders.Apply(p, GtsConfig.HasShrinkBuff(p) ? GtsConfig.GetScale(p) : 1f);
			}
			if (!GameManager.IsDedicatedServer)
			{
				GtsStompAnim.LateUpdate();
			}
		}

		private void ApplyVisualScale(EntityPlayer _player)
		{
			if (_player == null || _player.ModelTransform == null)
			{
				return;
			}
			int id = _player.entityId;
			float target = 1f;
			if (Squashed.ContainsKey(id) && _player.IsDead())
			{
				target = 0.001f;
			}
			if (target == 1f && GtsConfig.HasShrinkBuff(_player))
			{
				target = GtsConfig.GetScale(_player);
			}

			bool owned = appliedScale.ContainsKey(id);
			if (target == 1f && !owned)
			{
				return;
			}
			Vector3 cur = _player.ModelTransform.localScale;
			if (Mathf.Abs(cur.x - target) > 0.0001f || Mathf.Abs(cur.y - target) > 0.0001f)
			{
				_player.ModelTransform.localScale = Vector3.one * target;
			}
			if (target == 1f)
			{
				appliedScale.Remove(id);
			}
			else
			{
				appliedScale[id] = target;
			}
		}

		// ---------------- server ----------------

		private void CheckStomps(World _world, float _dt)
		{
			stompers.Clear();
			List<EntityPlayer> players = _world.Players.list;
			for (int i = 0; i < players.Count; i++)
			{
				stompers.Add(players[i]);
			}
			// zombies matter whenever someone is tiny: female ones may squash them, male ones fall apart on contact
			bool anyShrunk = false;
			for (int i = 0; i < players.Count && !anyShrunk; i++)
			{
				anyShrunk = GtsConfig.IsShrunk(players[i]);
			}
			if (anyShrunk)
			{
				List<Entity> entities = _world.Entities.list;
				for (int i = 0; i < entities.Count; i++)
				{
					if (entities[i] is EntityZombie zombie)
					{
						stompers.Add(zombie);
					}
				}
			}

			for (int v = 0; v < players.Count; v++)
			{
				EntityPlayer victim = players[v];
				if (!GtsConfig.IsShrunk(victim) || victim.AttachedToEntity != null)
				{
					continue;
				}
				float scale = GtsConfig.GetScale(victim);
				float radius = GtsConfig.StompFootRadius + GtsConfig.PlayerRadius * scale;
				float maxDY = GtsConfig.PlayerHeight * scale + GtsConfig.StompHeadroom;
				bool femaleZombiesStomp = GtsConfig.ZombiesCanStomp(victim);
				for (int s = 0; s < stompers.Count; s++)
				{
					EntityAlive stomper = stompers[s];
					if (stomper == victim || stomper.IsDead() || GtsConfig.IsShrunk(stomper) || stomper.AttachedToEntity != null)
					{
						continue;
					}
					Vector3 d = stomper.position - victim.position;
					float distSq = d.x * d.x + d.z * d.z;
					if (d.y < GtsConfig.StompMinDY || d.y > maxDY)
					{
						continue;
					}
					if (stomper is EntityZombie zombie)
					{
						if (!GtsZombies.IsFemale(zombie))
						{
							// "touching": the zombie body blocks the tiny player at about its own radius
							float touch = Mathf.Max(zombie.radius, 0.3f) + GtsConfig.PlayerRadius * scale + GtsConfig.MaleTouchMargin;
							if (distSq <= touch * touch)
							{
								// any contact is enough, moving or not; the victim survives
								Log.Out("[GTS] {0} touched {1} and fell apart", zombie.GetDebugName(), victim.GetDebugName());
								GtsZombies.Dismember(zombie, victim.position);
							}
						}
						else if (femaleZombiesStomp && distSq <= GtsConfig.StompTriggerRange * GtsConfig.StompTriggerRange)
						{
							BeginStomp(zombie, victim);
						}
						continue;
					}
					if (distSq > radius * radius || scale >= GtsConfig.PlayerStompMaxScale)
					{
						continue;
					}
					if (GetSpeed(stomper, _dt) < GtsConfig.StompMinSpeed)
					{
						continue;
					}
					Squash(_world, victim, stomper);
					break;
				}
			}

			// remember positions for next tick's speed calculation
			lastPos.Clear();
			for (int i = 0; i < stompers.Count; i++)
			{
				lastPos[stompers[i].entityId] = stompers[i].position;
			}
		}

		private void BeginStomp(EntityZombie _zombie, EntityPlayer _victim)
		{
			int id = _zombie.entityId;
			if (pendingStomps.ContainsKey(id) || (stompCooldownUntil.TryGetValue(id, out float until) && Time.time < until))
			{
				return;
			}
			float impact = GtsConfig.StompRaiseTime + GtsConfig.StompHoldTime + GtsConfig.StompSlamTime;
			PendingStomp ps = new PendingStomp { zombie = _zombie, victim = _victim, impactAt = Time.time + impact };
			pendingStomps[id] = ps;
			FaceVictim(ps);
			stompCooldownUntil[id] = Time.time + impact + GtsConfig.StompCooldown;

			SingletonMonoBehaviour<ConnectionManager>.Instance.SendPackage(NetPackageManager.GetPackage<NetPackageGtsStomp>().Setup(id));
			if (!GameManager.IsDedicatedServer)
			{
				GtsStompAnim.Play(_zombie);
			}
		}

		// The foot comes down: squash whoever tiny is still under it.
		private void ResolveStomps(World _world)
		{
			if (pendingStomps.Count == 0)
			{
				return;
			}
			foreach (KeyValuePair<int, PendingStomp> kv in pendingStomps)
			{
				PendingStomp ps = kv.Value;
				if (Time.time < ps.impactAt)
				{
					FaceVictim(ps);
					continue;
				}
				doneStomps.Add(kv.Key);
				EntityZombie z = ps.zombie;
				EntityPlayer v = ps.victim;
				if (z == null || z.IsDead() || v == null || !GtsConfig.IsShrunk(v) || !GtsConfig.ZombiesCanStomp(v))
				{
					continue;
				}
				Vector3 fwd = z.GetForwardVector();
				fwd.y = 0f;
				Vector3 foot = z.position + (fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.zero) * GtsConfig.StompFootReach;
				float scale = GtsConfig.GetScale(v);
				float hitRadius = GtsConfig.StompFootRadius + GtsConfig.PlayerRadius * scale + GtsConfig.StompHitLeniency;
				Vector3 d = v.position - foot;
				if (Mathf.Abs(d.y) <= GtsConfig.PlayerHeight * scale + GtsConfig.StompHeadroom && d.x * d.x + d.z * d.z <= hitRadius * hitRadius)
				{
					Squash(_world, v, z);
				}
			}
			foreach (int id in doneStomps)
			{
				pendingStomps.Remove(id);
			}
			doneStomps.Clear();
		}

		// While winding up, the zombie stops and turns to the victim so the foot comes down on them.
		// Rotation is replicated to every client by the game.
		private static void FaceVictim(PendingStomp _ps)
		{
			if (_ps.zombie == null || _ps.zombie.IsDead() || _ps.victim == null)
			{
				return;
			}
			Vector3 d = _ps.victim.position - _ps.zombie.position;
			if (d.x * d.x + d.z * d.z > 0.0001f)
			{
				float yaw = 90f - Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg;
				_ps.zombie.SetRotationAndStopTurning(new Vector3(0f, yaw, 0f));
			}
			if (_ps.zombie.moveHelper != null)
			{
				_ps.zombie.moveHelper.Stop();
			}
		}

		private float GetSpeed(EntityAlive _e, float _dt)
		{
			if (!lastPos.TryGetValue(_e.entityId, out Vector3 prev) || _dt <= 0f)
			{
				return 0f;
			}
			Vector3 delta = _e.position - prev;
			delta.y = 0f;
			return delta.magnitude / _dt;
		}

		private void Squash(World _world, EntityPlayer _victim, EntityAlive _stomper)
		{
			Vector3 pos = _victim.position;
			Log.Out("[GTS] {0} squashed {1}", _stomper.GetDebugName(), _victim.GetDebugName());

			// FX first so clients hide the body before the death animation starts
			float scale = GtsConfig.GetScale(_victim);
			NetPackageGtsFx pkg = NetPackageManager.GetPackage<NetPackageGtsFx>().Setup(_victim.entityId, pos, scale);
			SingletonMonoBehaviour<ConnectionManager>.Instance.SendPackage(pkg);
			if (!GameManager.IsDedicatedServer)
			{
				NetPackageGtsFx.PlayLocal(_victim.entityId, pos, scale);
			}

			// no attacker entity on the damage source, so the server's PvP setting can't block it
			DamageSource src = new DamageSource(EnumDamageSource.External, EnumDamageTypes.Crushing, Vector3.down);
			_victim.DamageEntity(src, 99999, false, 1f);

			if (GtsConfig.UseBloodBlocks)
			{
				SpillBlood(_world, pos);
			}
		}

		private void SpillBlood(World _world, Vector3 _pos)
		{
			Vector3i center = World.worldToBlockPos(_pos);
			List<BlockChangeInfo> changes = new List<BlockChangeInfo>();
			// a single pool right where the victim was
			if (FindFloor(_world, center, out Vector3i p))
			{
				BlockValue bv = Block.GetBlockValue(GtsConfig.BloodBlocks[Random.Range(0, GtsConfig.BloodBlocks.Length)]);
				if (!bv.isair)
				{
					bv.rotation = (byte)Random.Range(0, 4);
					changes.Add(new BlockChangeInfo(p, bv, true));
					bloodBlocks.Add(new BloodBlock { pos = p, type = bv.type, removeAt = Time.time + GtsConfig.BloodLifetime });
				}
			}
			if (changes.Count > 0)
			{
				_world.SetBlocksRPC(changes);
			}
		}

		// Air block standing on something solid, searched one block up and down.
		private static bool FindFloor(World _world, Vector3i _start, out Vector3i _pos)
		{
			for (int dy = 0; dy >= -1; dy--)
			{
				Vector3i p = _start + new Vector3i(0, dy, 0);
				if (_world.GetBlock(p).isair && _world.GetBlock(p + Vector3i.down).Block.IsCollideMovement)
				{
					_pos = p;
					return true;
				}
			}
			Vector3i up = _start + Vector3i.up;
			if (_world.GetBlock(up).isair && _world.GetBlock(_start).Block.IsCollideMovement)
			{
				_pos = up;
				return true;
			}
			_pos = Vector3i.zero;
			return false;
		}

		private void CleanupBlood(World _world)
		{
			if (bloodBlocks.Count == 0)
			{
				return;
			}
			List<BlockChangeInfo> changes = null;
			for (int i = bloodBlocks.Count - 1; i >= 0; i--)
			{
				BloodBlock b = bloodBlocks[i];
				if (Time.time < b.removeAt)
				{
					continue;
				}
				bloodBlocks.RemoveAt(i);
				if (_world.GetBlock(b.pos).type == b.type)
				{
					changes = changes ?? new List<BlockChangeInfo>();
					changes.Add(new BlockChangeInfo(b.pos, BlockValue.Air, true));
				}
			}
			if (changes != null)
			{
				_world.SetBlocksRPC(changes);
			}
		}
	}
}
