using System.Collections.Generic;
using UnityEngine;

namespace GTS
{
	// Plays GtsMoves on a zombie: after the Animator has posed it each frame, the hips are lowered / moved toward
	// the working foot so the knee never locks straight, and both legs are bent with a two-bone IK so the feet
	// follow the move's trajectory around the victim (the other foot stays where the animation put it). The
	// thigh's twist and the foot's heading are set explicitly so knees and toes point where she faces.
	// Consecutive moves cross-fade; when a move ends it holds its last pose while the victim is still pinned,
	// otherwise it eases back into the normal animation.
	// Purely visual and local; the server picks the moves (NetPackageGtsMove) and deals the damage.
	public static class GtsMoveAnim
	{
		private const float CrossFade = 0.15f;
		private const float FadeOut = 0.3f;

		private class Leg
		{
			public Transform thigh, shin, foot, toe;
			public float side;
			public float length;         // hip to ankle, straight
			public float footLen;        // ankle to ball
			public Vector3 lastTarget;   // Unity space, what we drove the ankle to last frame
			public float lastWeight, lastPitch, lastTwist;
			public Vector3 fromTarget;   // cross-fade start
			public float fromWeight, fromPitch, fromTwist;
			// this frame
			public Vector3 animPos;
			public Quaternion animRot;
			public Vector3 animToe;
			public Vector3 goal, wantToe;
			public float weight, yaw;
		}

		private class Active
		{
			public EntityAlive zombie;
			public EntityAlive victim;
			public Vector3 victimPos;    // world, last known
			public Transform spine, hips;
			public Leg right, left;
			public float soleDrop;       // degrees the ankle-to-ball line points down with the sole flat
			public GtsMove move;
			public float start;
			public float fadeStart = -1f;
			public float lastLean, fromLean, lastCrouch, fromCrouch;
		}

		private static readonly Dictionary<int, Active> active = new Dictionary<int, Active>();
		private static readonly List<int> finished = new List<int>();
		private static readonly HashSet<int> holding = new HashSet<int>();   // zombies whose victim is pinned

		public static void Play(EntityAlive _zombie, EntityAlive _victim, GtsMove _move)
		{
			if (_zombie == null)
			{
				return;
			}
			if (!active.TryGetValue(_zombie.entityId, out Active a))
			{
				a = Create(_zombie);
				if (a == null)
				{
					return;
				}
				active[_zombie.entityId] = a;
			}
			// cross-fade from wherever the legs are now
			foreach (Leg l in new[] { a.right, a.left })
			{
				l.fromTarget = l.lastTarget;
				l.fromWeight = l.lastWeight;
				l.fromPitch = l.lastPitch;
				l.fromTwist = l.lastTwist;
			}
			a.fromLean = a.lastLean;
			a.fromCrouch = a.lastCrouch;
			a.victim = _victim;
			if (_victim != null)
			{
				a.victimPos = _victim.position;
			}
			a.move = _move;
			a.start = Time.time;
			a.fadeStart = -1f;
		}

		public static void SetHolding(EntityAlive _zombie, bool _hold)
		{
			if (_zombie == null)
			{
				return;
			}
			if (_hold)
			{
				holding.Add(_zombie.entityId);
			}
			else
			{
				holding.Remove(_zombie.entityId);
				if (active.TryGetValue(_zombie.entityId, out Active a) && a.fadeStart < 0f)
				{
					a.fadeStart = Time.time;
				}
			}
		}

		public static void Reset()
		{
			active.Clear();
			holding.Clear();
		}

		private static Active Create(EntityAlive _zombie)
		{
			Transform model = _zombie.emodel != null ? _zombie.emodel.GetModelTransform() : null;
			if (model == null)
			{
				return null;
			}
			Active a = new Active
			{
				zombie = _zombie,
				spine = model.FindInChilds("Spine1") ?? model.FindInChilds("Spine"),
				right = FindLeg(model, "Right", 1f),
				left = FindLeg(model, "Left", -1f)
			};
			if (a.right == null || a.left == null)
			{
				Log.Warning("[GTS] moves: leg bones not found on " + _zombie.GetDebugName());
				return null;
			}
			a.hips = model.FindInChilds("Hips") ?? a.right.thigh.parent;
			// sole angle, measured on whichever foot is lower (the one standing on the ground)
			Leg low = a.right.foot.position.y <= a.left.foot.position.y ? a.right : a.left;
			a.soleDrop = 15f;
			if (low.toe != null)
			{
				Vector3 d = low.toe.position - low.foot.position;
				a.soleDrop = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(-d.y / d.magnitude, -1f, 1f)) * Mathf.Rad2Deg, 5f, 35f);
			}
			a.right.lastTarget = a.right.foot.position;
			a.left.lastTarget = a.left.foot.position;
			return a;
		}

		private static Leg FindLeg(Transform _model, string _side, float _sign)
		{
			Leg l = new Leg
			{
				side = _sign,
				thigh = _model.FindInChilds(_side + "UpLeg"),
				shin = _model.FindInChilds(_side + "Leg"),
				foot = _model.FindInChilds(_side + "Foot"),
				toe = _model.FindInChilds(_side + "ToeBase")
			};
			if (l.thigh == null || l.shin == null || l.foot == null)
			{
				return null;
			}
			l.length = (l.shin.position - l.thigh.position).magnitude + (l.foot.position - l.shin.position).magnitude;
			l.footLen = l.toe != null ? (l.toe.position - l.foot.position).magnitude : 0.13f;
			return l;
		}

		// Called from GtsDriver.LateUpdate, i.e. after this frame's animation has been applied.
		public static void LateUpdate()
		{
			if (active.Count == 0)
			{
				return;
			}
			foreach (KeyValuePair<int, Active> kv in active)
			{
				Active a = kv.Value;
				if (a.zombie == null || a.zombie.IsDead() || a.right.thigh == null)
				{
					finished.Add(kv.Key);
					continue;
				}
				if (a.victim != null && !a.victim.IsDead())
				{
					a.victimPos = a.victim.position;
				}
				float t = Time.time - a.start;
				float len = GtsMoves.Length(a.move);
				if (a.fadeStart < 0f && t > len && !holding.Contains(kv.Key))
				{
					a.fadeStart = Time.time;
				}
				float fade = 1f;
				if (a.fadeStart >= 0f)
				{
					fade = 1f - (Time.time - a.fadeStart) / FadeOut;
					if (fade <= 0f)
					{
						finished.Add(kv.Key);
						continue;
					}
				}
				Apply(a, GtsMoves.Evaluate(a.move, Mathf.Min(t, len)), Mathf.Clamp01(t / CrossFade), fade);
			}
			foreach (int id in finished)
			{
				active.Remove(id);
			}
			finished.Clear();
		}

		private static void Apply(Active _a, GtsMovePose _p, float _blend, float _fade)
		{
			Transform zt = _a.zombie.transform;
			Vector3 right = zt.right;
			right.y = 0f;
			right = right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.right;
			Vector3 fwd = Vector3.Cross(right, Vector3.up);
			Vector3 origin = _a.victimPos - Origin.position;

			Goal(_a, _a.right, _p.right, origin, right, fwd, _blend, _fade);
			Goal(_a, _a.left, _p.left, origin, right, fwd, _blend, _fade);

			float lean = Mathf.Lerp(_a.fromLean, _p.lean, _blend) * _fade;
			_a.lastLean = lean;
			if (_a.spine != null && Mathf.Abs(lean) > 0.01f)
			{
				_a.spine.rotation = Quaternion.AngleAxis(-lean * GtsConfig.StompAxisSign, right) * _a.spine.rotation;
			}

			// hips: down by the crouch, then toward any foot that would otherwise need a straight leg
			float crouch = Mathf.Lerp(_a.fromCrouch, _p.crouch, _blend) * _fade;
			_a.lastCrouch = crouch;
			Vector3 shift = Vector3.down * crouch;
			foreach (Leg l in new[] { _a.right, _a.left })
			{
				if (l.weight <= 0.001f)
				{
					continue;
				}
				Vector3 v = Vector3.Lerp(l.animPos, l.goal, l.weight) - (l.thigh.position + shift);
				float excess = v.magnitude - GtsConfig.LegReach * l.length;
				if (excess > 0f)
				{
					shift += v.normalized * excess * l.weight;
				}
			}
			shift.y = Mathf.Min(shift.y, 0f);
			shift = Vector3.ClampMagnitude(shift, GtsConfig.MaxHipShift);
			bool moved = shift.sqrMagnitude > 0.000001f;
			if (moved && _a.hips != null)
			{
				_a.hips.position += shift;
			}

			SolveFoot(_a.right, right, fwd, moved);
			SolveFoot(_a.left, right, fwd, moved);
		}

		// Where this foot should go this frame (the cross-faded ankle target, weight and heading).
		private static void Goal(Active _a, Leg _leg, GtsLegPose _pose, Vector3 _origin, Vector3 _right, Vector3 _fwd, float _blend, float _fade)
		{
			_leg.animPos = _leg.foot.position;
			_leg.animRot = _leg.foot.rotation;
			_leg.animToe = _leg.toe != null ? _leg.toe.position - _leg.foot.position : Vector3.zero;

			float pitch = Mathf.Lerp(_leg.fromPitch, _pose.pitch, _blend);
			float twist = Mathf.Lerp(_leg.fromTwist, _pose.twist, _blend);
			_leg.lastPitch = pitch;
			_leg.lastTwist = twist;
			_leg.yaw = _leg.side * GtsConfig.FootSplay + twist;
			Vector3 toe = Quaternion.AngleAxis(_leg.yaw, Vector3.up) * _fwd;
			toe = Quaternion.AngleAxis(_a.soleDrop - pitch, Vector3.Cross(Vector3.up, toe)) * toe;
			_leg.wantToe = toe;

			Vector3 goal = _origin + _right * _pose.target.x + Vector3.up * _pose.target.y + _fwd * _pose.target.z;
			if (_pose.toePivot)
			{
				goal -= toe * _leg.footLen;
			}
			// cross-fade target and weight from the previous move
			_leg.goal = Vector3.Lerp(_leg.fromWeight > 0.001f ? _leg.fromTarget : goal, goal, _blend);
			_leg.weight = Mathf.Lerp(_leg.fromWeight, _pose.weight, _blend) * _fade;
			_leg.lastTarget = _leg.goal;
			_leg.lastWeight = _leg.weight;
		}

		private static void SolveFoot(Leg _leg, Vector3 _right, Vector3 _fwd, bool _hipsMoved)
		{
			if (_leg.weight <= 0.001f && !_hipsMoved)
			{
				return;
			}
			// an idle foot (weight 0) is put back where the animation had it, so it stays planted under lowered hips
			Vector3 kneeDir = (Quaternion.AngleAxis(_leg.yaw * 0.6f, Vector3.up) * _fwd + _right * (_leg.side * GtsConfig.KneeOut)).normalized;
			SolveLeg(_leg, Vector3.Lerp(_leg.animPos, _leg.goal, _leg.weight), kneeDir, _fwd, _right);
			if (_leg.toe != null && _leg.animToe.sqrMagnitude > 0.000001f)
			{
				Quaternion aimed = Quaternion.FromToRotation(_leg.animToe, _leg.wantToe) * _leg.animRot;
				_leg.foot.rotation = Quaternion.Slerp(_leg.animRot, aimed, _leg.weight);
			}
		}

		// Plain two-bone IK for any chain (root, middle, end): the end reaches _target, the middle joint bends
		// toward _bendDir. Used for the pinned victim's arms.
		public static void TwoBone(Transform _root, Transform _mid, Transform _end, Vector3 _target, Vector3 _bendDir)
		{
			Vector3 a = _root.position;
			Vector3 b = _mid.position;
			Vector3 c = _end.position;
			float l1 = (b - a).magnitude;
			float l2 = (c - b).magnitude;
			Vector3 toTarget = _target - a;
			if (l1 < 0.0001f || l2 < 0.0001f || toTarget.sqrMagnitude < 0.00000001f)
			{
				return;
			}
			float d = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(l1 - l2) + 0.0001f, l1 + l2 - 0.0001f);
			Vector3 dir = toTarget.normalized;
			Vector3 bendAxis = Vector3.Cross(dir, _bendDir);
			if (bendAxis.sqrMagnitude < 0.000001f)
			{
				return;
			}
			bendAxis.Normalize();
			float cosA = Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f);
			Vector3 midWanted = a + Quaternion.AngleAxis(Mathf.Acos(cosA) * Mathf.Rad2Deg, bendAxis) * dir * l1;
			_root.rotation = Quaternion.FromToRotation(b - a, midWanted - a) * _root.rotation;
			Vector3 b2 = _mid.position;
			_mid.rotation = Quaternion.FromToRotation(_end.position - b2, a + dir * d - b2) * _mid.rotation;
		}

		// Two-bone IK: bend thigh and shin so the ankle reaches _target, knee pointing toward _kneeDir. The thigh's
		// roll is set too (its kneecap, assumed to face her forward in the animation, turned to face _kneeDir).
		private static void SolveLeg(Leg _l, Vector3 _target, Vector3 _kneeDir, Vector3 _fwd, Vector3 _right)
		{
			Vector3 a = _l.thigh.position;
			Vector3 b = _l.shin.position;
			Vector3 c = _l.foot.position;
			float l1 = (b - a).magnitude;
			float l2 = (c - b).magnitude;
			Vector3 toTarget = _target - a;
			if (l1 < 0.001f || l2 < 0.001f || toTarget.sqrMagnitude < 0.000001f)
			{
				return;
			}
			float d = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(l1 - l2) + 0.001f, l1 + l2 - 0.001f);
			Vector3 dir = toTarget.normalized;
			Vector3 bendAxis = Vector3.Cross(dir, _kneeDir);
			if (bendAxis.sqrMagnitude < 0.000001f)
			{
				bendAxis = _right;
			}
			bendAxis.Normalize();
			float cosA = Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f);
			// a positive angle about Cross(dir, kneeDir) turns dir toward kneeDir
			Vector3 kneeWanted = a + Quaternion.AngleAxis(Mathf.Acos(cosA) * Mathf.Rad2Deg, bendAxis) * dir * l1;

			Vector3 axis0 = b - a;
			Vector3 axis1 = kneeWanted - a;
			Vector3 face0 = Vector3.ProjectOnPlane(_fwd, axis0);
			Vector3 face1 = Vector3.ProjectOnPlane(_kneeDir, axis1);
			if (face0.sqrMagnitude > 0.0001f && face1.sqrMagnitude > 0.0001f)
			{
				_l.thigh.rotation = Quaternion.LookRotation(axis1, face1) * Quaternion.Inverse(Quaternion.LookRotation(axis0, face0)) * _l.thigh.rotation;
			}
			else
			{
				_l.thigh.rotation = Quaternion.FromToRotation(axis0, axis1) * _l.thigh.rotation;
			}
			Vector3 b2 = _l.shin.position;
			Vector3 c2 = _l.foot.position;
			_l.shin.rotation = Quaternion.FromToRotation(c2 - b2, a + dir * d - b2) * _l.shin.rotation;
		}
	}

	// Server -> clients: this zombie starts this move on this victim.
	public class NetPackageGtsMove : NetPackage
	{
		private int zombieId;
		private int victimId;
		private byte move;

		public override NetPackageDirection PackageDirection => NetPackageDirection.ToClient;

		public NetPackageGtsMove Setup(int _zombieId, int _victimId, GtsMove _move)
		{
			zombieId = _zombieId;
			victimId = _victimId;
			move = (byte)_move;
			return this;
		}

		public override void read(PooledBinaryReader _br)
		{
			zombieId = _br.ReadInt32();
			victimId = _br.ReadInt32();
			move = _br.ReadByte();
		}

		public override void write(PooledBinaryWriter _bw)
		{
			base.write(_bw);
			_bw.Write(zombieId);
			_bw.Write(victimId);
			_bw.Write(move);
		}

		public override void ProcessPackage(World _world, GameManager _callbacks)
		{
			if (_world != null)
			{
				GtsMoveAnim.Play(_world.GetEntity(zombieId) as EntityAlive, _world.GetEntity(victimId) as EntityAlive, (GtsMove)move);
			}
		}

		public override int GetLength()
		{
			return 9;
		}
	}
}
