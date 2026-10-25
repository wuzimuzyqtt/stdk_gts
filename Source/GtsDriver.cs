using System.Collections.Generic;
using UnityEngine;

namespace GTS
{
	// Runs on every machine.
	//  - Client: keeps every player's model at the right visual scale, hides squashed bodies.
	//  - Server: detects stomps, kills the victim, spills blood blocks and cleans them up later.
	public class GtsDriver : MonoBehaviour
	{
		private struct BloodBlock
		{
			public Vector3i pos;
			public int type;
			public float removeAt;
		}

		// Players whose body should stay hidden until they respawn (client side).
		public static readonly HashSet<int> Squashed = new HashSet<int>();

		// Model scale we last applied per entity, so we only touch models we changed ourselves.
		private readonly Dictionary<int, float> appliedScale = new Dictionary<int, float>();

		private readonly Dictionary<int, Vector3> lastPos = new Dictionary<int, Vector3>();
		private readonly List<BloodBlock> bloodBlocks = new List<BloodBlock>();
		private readonly List<EntityAlive> stompers = new List<EntityAlive>();
		private float lastCheck;

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
			CleanupBlood(world);
		}

		private void LateUpdate()
		{
			if (GameManager.IsDedicatedServer)
			{
				return;
			}
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
				ApplyVisualScale(p);
				GtsColliders.Apply(p, GtsConfig.IsShrunk(p) ? GtsConfig.GetScale(p) : 1f);
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
			if (Squashed.Contains(id))
			{
				if (_player.IsDead())
				{
					target = 0.001f;
				}
				else
				{
					Squashed.Remove(id);
				}
			}
			if (target == 1f && GtsConfig.IsShrunk(_player))
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
			if (GtsConfig.ZombiesCanStomp)
			{
				List<Entity> entities = _world.Entities.list;
				for (int i = 0; i < entities.Count; i++)
				{
					if (entities[i] is EntityEnemy enemy)
					{
						stompers.Add(enemy);
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
				for (int s = 0; s < stompers.Count; s++)
				{
					EntityAlive stomper = stompers[s];
					if (stomper == victim || stomper.IsDead() || GtsConfig.IsShrunk(stomper) || stomper.AttachedToEntity != null)
					{
						continue;
					}
					Vector3 d = stomper.position - victim.position;
					if (d.y < GtsConfig.StompMinDY || d.y > GtsConfig.StompMaxDY)
					{
						continue;
					}
					if (d.x * d.x + d.z * d.z > GtsConfig.StompRadius * GtsConfig.StompRadius)
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
			NetPackageGtsFx pkg = NetPackageManager.GetPackage<NetPackageGtsFx>().Setup(_victim.entityId, pos);
			SingletonMonoBehaviour<ConnectionManager>.Instance.SendPackage(pkg);
			if (!GameManager.IsDedicatedServer)
			{
				NetPackageGtsFx.PlayLocal(_victim.entityId, pos);
			}

			// no attacker entity on the damage source, so the server's PvP setting can't block it
			DamageSource src = new DamageSource(EnumDamageSource.External, EnumDamageTypes.Crushing, Vector3.down);
			_victim.DamageEntity(src, 99999, false, 1f);

			SpillBlood(_world, pos);
		}

		private void SpillBlood(World _world, Vector3 _pos)
		{
			Vector3i center = World.worldToBlockPos(_pos);
			List<BlockChangeInfo> changes = new List<BlockChangeInfo>();
			Vector3i[] offsets =
			{
				Vector3i.zero, new Vector3i(1, 0, 0), new Vector3i(-1, 0, 0), new Vector3i(0, 0, 1), new Vector3i(0, 0, -1)
			};
			for (int i = 0; i < offsets.Length; i++)
			{
				if (i > 0 && Random.value < 0.4f)
				{
					continue;
				}
				if (!FindFloor(_world, center + offsets[i], out Vector3i p))
				{
					continue;
				}
				BlockValue bv = Block.GetBlockValue(GtsConfig.BloodBlocks[Random.Range(0, GtsConfig.BloodBlocks.Length)]);
				if (bv.isair)
				{
					continue;
				}
				bv.rotation = (byte)Random.Range(0, 4);
				changes.Add(new BlockChangeInfo(p, bv, true));
				bloodBlocks.Add(new BloodBlock { pos = p, type = bv.type, removeAt = Time.time + GtsConfig.BloodLifetime });
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
