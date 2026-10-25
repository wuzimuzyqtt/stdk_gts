using System.Collections.Generic;
using UnityEngine;

namespace GTS
{
	// Scales a player's physical colliders to match their visual size.
	//  - Local player: the Unity CharacterController driven by vp_FPController (what you actually walk with).
	//  - Every player (local and remote): the capsule on PhysicsTransform and the LargeEntityBlocker body,
	//    which is what other players bump into or stand on.
	public static class GtsColliders
	{
		private class LocalCC
		{
			public float normalHeight;
			public float stepOffset;
			public float skinWidth;
			public float minMoveDistance;
			public float applied = 1f;
		}

		private struct Shape
		{
			public Vector3 center;
			public Vector3 size;   // box size, or (radius, height, 0) for capsules, (radius, 0, 0) for spheres
		}

		private class Tracked
		{
			public Shape orig;
			public Shape applied;
		}

		private static LocalCC localCC;
		private static readonly Dictionary<Collider, Tracked> tracked = new Dictionary<Collider, Tracked>();
		private static readonly Dictionary<int, List<Collider>> byEntity = new Dictionary<int, List<Collider>>();
		private static readonly List<Collider> tmp = new List<Collider>();

		public static void Reset()
		{
			localCC = null;
			tracked.Clear();
			byEntity.Clear();
		}

		public static void Apply(EntityPlayer _player, float _scale)
		{
			if (_player is EntityPlayerLocal local)
			{
				ApplyLocalCC(local, _scale);
			}
			ApplyBody(_player, _scale);
		}

		// ---------------- local CharacterController ----------------

		private static void ApplyLocalCC(EntityPlayerLocal _player, float _scale)
		{
			vp_FPController fpc = _player.vp_FPController;
			if (fpc == null || fpc.CharacterController == null)
			{
				return;
			}
			CharacterController cc = fpc.CharacterController;
			if (localCC == null)
			{
				if (_scale == 1f)
				{
					return;
				}
				localCC = new LocalCC
				{
					normalHeight = fpc.m_NormalHeight,
					stepOffset = fpc.m_StepHeight,
					skinWidth = cc.skinWidth,
					minMoveDistance = cc.minMoveDistance
				};
			}
			float wantHeight = localCC.normalHeight * _scale;
			if (localCC.applied == _scale && Mathf.Approximately(fpc.m_NormalHeight, wantHeight))
			{
				return;
			}

			// Unity rejects a step offset taller than the controller, so lower the step before shrinking
			// and raise it after growing.
			float wantStep = localCC.stepOffset * _scale;
			if (wantHeight < cc.height)
			{
				cc.stepOffset = wantStep;
				cc.height = wantHeight;
			}
			else
			{
				cc.height = wantHeight;
				cc.stepOffset = wantStep;
			}
			cc.skinWidth = Mathf.Clamp(localCC.skinWidth * _scale, 0.0005f, localCC.skinWidth);
			// tiny players move less than the default min distance per frame and would otherwise not move at all
			cc.minMoveDistance = _scale < 1f ? 0f : localCC.minMoveDistance;
			fpc.SyncCharacterController();   // recomputes center, radius, crouch height and step height from cc.height
			fpc.RefreshColliders();          // re-applies the current crouch state

			localCC.applied = _scale;
			if (_scale == 1f)
			{
				localCC = null;
			}
		}

		// Original standing size of the local player's controller, for the "room to grow" check.
		public static bool CanGrow(EntityPlayerLocal _player, float _newScale, out string _blocker)
		{
			_blocker = null;
			vp_FPController fpc = _player.vp_FPController;
			if (localCC == null || fpc == null || _newScale <= localCC.applied)
			{
				return true;
			}
			CharacterController cc = fpc.CharacterController;
			float height = localCC.normalHeight * _newScale;
			float radius = height * 0.16666f * 0.9f;
			Vector3 feet = fpc.transform.position;
			Vector3 bottom = feet + Vector3.up * (radius + localCC.stepOffset * _newScale + 0.02f);
			Vector3 top = feet + Vector3.up * (height - radius);
			if (top.y < bottom.y)
			{
				top = bottom;
			}

			int layer = cc.gameObject.layer;
			int mask = 0;
			for (int i = 0; i < 32; i++)
			{
				if (!Physics.GetIgnoreLayerCollision(layer, i))
				{
					mask |= 1 << i;
				}
			}
			foreach (Collider c in Physics.OverlapCapsule(bottom, top, radius, mask, QueryTriggerInteraction.Ignore))
			{
				Entity owner = c.GetComponentInParent<Entity>();
				if (owner == _player || (_player.physicsRB != null && c.attachedRigidbody == _player.physicsRB))
				{
					continue;
				}
				_blocker = owner != null ? owner.GetDebugName() : c.name;
				return false;
			}
			return true;
		}

		// ---------------- body colliders (all players) ----------------

		private static void ApplyBody(EntityPlayer _player, float _scale)
		{
			int id = _player.entityId;
			if (!byEntity.TryGetValue(id, out List<Collider> list))
			{
				if (_scale == 1f)
				{
					return;
				}
				list = new List<Collider>();
				Collect(_player.PhysicsTransform, list);
				Collect(_player.physicsRBT, list);
				byEntity[id] = list;
			}

			for (int i = list.Count - 1; i >= 0; i--)
			{
				Collider c = list[i];
				if (c == null)
				{
					list.RemoveAt(i);
					continue;
				}
				if (!tracked.TryGetValue(c, out Tracked t))
				{
					t = new Tracked { orig = Read(c) };
					t.applied = t.orig;
					tracked[c] = t;
				}
				Shape cur = Read(c);
				if (!Same(cur, t.applied))
				{
					// the game resized it since we last touched it: take that as the new full-size shape
					t.orig = cur;
				}
				Shape want = Scaled(t.orig, _scale);
				if (!Same(cur, want))
				{
					Write(c, want);
				}
				t.applied = want;
				if (_scale == 1f)
				{
					tracked.Remove(c);
				}
			}
			if (_scale == 1f)
			{
				byEntity.Remove(id);
			}
		}

		private static void Collect(Transform _root, List<Collider> _out)
		{
			if (_root == null)
			{
				return;
			}
			_root.GetComponentsInChildren(true, tmp);
			foreach (Collider c in tmp)
			{
				// the local CharacterController is handled through vp_FPController; triggers don't block anyone
				if (c is CharacterController || c.isTrigger || _out.Contains(c))
				{
					continue;
				}
				if (c is CapsuleCollider || c is BoxCollider || c is SphereCollider)
				{
					_out.Add(c);
				}
			}
			tmp.Clear();
		}

		private static Shape Read(Collider _c)
		{
			switch (_c)
			{
				case CapsuleCollider cap:
					return new Shape { center = cap.center, size = new Vector3(cap.radius, cap.height, 0f) };
				case BoxCollider box:
					return new Shape { center = box.center, size = box.size };
				case SphereCollider sph:
					return new Shape { center = sph.center, size = new Vector3(sph.radius, 0f, 0f) };
			}
			return default;
		}

		private static void Write(Collider _c, Shape _s)
		{
			switch (_c)
			{
				case CapsuleCollider cap:
					cap.center = _s.center;
					cap.radius = _s.size.x;
					cap.height = _s.size.y;
					break;
				case BoxCollider box:
					box.center = _s.center;
					box.size = _s.size;
					break;
				case SphereCollider sph:
					sph.center = _s.center;
					sph.radius = _s.size.x;
					break;
			}
		}

		private static Shape Scaled(Shape _s, float _scale)
		{
			return new Shape { center = _s.center * _scale, size = _s.size * _scale };
		}

		private static bool Same(Shape _a, Shape _b)
		{
			return (_a.center - _b.center).sqrMagnitude < 1e-10f && (_a.size - _b.size).sqrMagnitude < 1e-10f;
		}
	}
}
