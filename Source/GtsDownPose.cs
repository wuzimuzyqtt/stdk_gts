using System.Collections.Generic;
using UnityEngine;

namespace GTS
{
	// A pinned victim's body: knocked onto their back with their feet toward the zombie and their chest under her
	// foot, both arms up pushing against the sole, trembling, buckling a little with every landing. Applied after
	// the Animator each frame (hips turned and moved, arms bent with a two-bone IK); visual only, on every client.
	public static class GtsDownPose
	{
		private const float FallTime = 0.25f;     // seconds to go down / get back up
		private const float HipsLift = 0.1f;      // hips above the ground lying down, full size
		private const float ChestOffset = 0.3f;   // the hips sit this far toward her from the victim's centre, full size
		private const float ArmReach = 0.85f;     // hands this fraction of the arm's length above the shoulders
		private const float Buckle = 0.35f;       // arms fold this much more on a landing...
		private const float BuckleTime = 0.3f;    // ...recovering over this long

		private class Arm
		{
			public Transform upper, fore, hand;
			public float side;
		}

		private class Down
		{
			public EntityAlive victim;
			public int zombieId;
			public Transform hips;
			public Arm left, right;
			public float start;
			public float releaseStart = -1f;
			public float lastImpact = -10f;
			public Vector3 toZombie = Vector3.forward;   // last known, kept while getting up
			public float k;                              // 0 standing .. 1 lying, this frame
		}

		private static readonly Dictionary<int, Down> downs = new Dictionary<int, Down>();
		private static readonly List<int> finished = new List<int>();
		private static readonly HashSet<string> warned = new HashSet<string>();

		public static void Pin(EntityAlive _victim, EntityAlive _zombie)
		{
			if (_victim == null)
			{
				return;
			}
			if (downs.TryGetValue(_victim.entityId, out Down d))
			{
				d.zombieId = _zombie != null ? _zombie.entityId : d.zombieId;
				d.releaseStart = -1f;
				return;
			}
			Transform model = _victim.emodel != null ? _victim.emodel.GetModelTransform() : null;
			if (model == null)
			{
				return;
			}
			d = new Down
			{
				victim = _victim,
				zombieId = _zombie != null ? _zombie.entityId : -1,
				hips = model.FindInChilds("Hips"),
				left = FindArm(model, "Left", -1f),
				right = FindArm(model, "Right", 1f),
				start = Time.time
			};
			if (d.hips == null)
			{
				if (warned.Add(model.name))
				{
					Log.Warning("[GTS] down pose: no Hips bone on " + _victim.GetDebugName());
				}
				return;
			}
			downs[_victim.entityId] = d;
		}

		public static void Impact(int _victimId)
		{
			if (downs.TryGetValue(_victimId, out Down d))
			{
				d.lastImpact = Time.time;
			}
		}

		public static void Release(int _victimId)
		{
			if (downs.TryGetValue(_victimId, out Down d) && d.releaseStart < 0f)
			{
				d.releaseStart = Time.time;
			}
		}

		public static void Reset()
		{
			downs.Clear();
		}

		// For the victim's own first-person camera: how far down they are (0..1), which way she is, and her.
		public static bool Get(int _victimId, out float _k, out Vector3 _toZombie, out int _zombieId)
		{
			if (downs.TryGetValue(_victimId, out Down d))
			{
				_k = d.k;
				_toZombie = d.toZombie;
				_zombieId = d.zombieId;
				return true;
			}
			_k = 0f;
			_toZombie = Vector3.forward;
			_zombieId = -1;
			return false;
		}

		private static Arm FindArm(Transform _model, string _side, float _sign)
		{
			Arm a = new Arm
			{
				side = _sign,
				upper = _model.FindInChilds(_side + "Arm"),
				fore = _model.FindInChilds(_side + "ForeArm"),
				hand = _model.FindInChilds(_side + "Hand")
			};
			return a.upper != null && a.fore != null && a.hand != null ? a : null;
		}

		// Called from GtsDriver.LateUpdate on clients, after this frame's animation.
		public static void LateUpdate(World _world)
		{
			if (downs.Count == 0)
			{
				return;
			}
			foreach (KeyValuePair<int, Down> kv in downs)
			{
				Down d = kv.Value;
				if (d.victim == null || d.victim.IsDead() || d.hips == null)
				{
					finished.Add(kv.Key);
					continue;
				}
				float k = Mathf.Clamp01((Time.time - d.start) / FallTime);
				if (d.releaseStart >= 0f)
				{
					k = Mathf.Min(k, 1f - (Time.time - d.releaseStart) / FallTime);
					if (k <= 0f)
					{
						finished.Add(kv.Key);
						continue;
					}
				}
				d.k = GtsMoves.EaseInOut(k);
				Apply(_world, d, d.k);
			}
			foreach (int id in finished)
			{
				downs.Remove(id);
			}
			finished.Clear();
		}

		private static void Apply(World _world, Down _d, float _k)
		{
			EntityAlive v = _d.victim;
			float scale = GtsConfig.HasShrinkBuff(v) ? GtsConfig.GetScale(v) : 1f;
			if (_world.GetEntity(_d.zombieId) is EntityAlive z && !z.IsDead())
			{
				Vector3 to = z.position - v.position;
				to.y = 0f;
				if (to.sqrMagnitude > 0.0001f)
				{
					_d.toZombie = to.normalized;
				}
			}

			// hips: turn so the body's forward points at her, then tip over backwards (feet toward her, face up)
			Transform model = v.emodel.GetModelTransform();
			Vector3 modelFwd = model.forward;
			modelFwd.y = 0f;
			modelFwd = modelFwd.sqrMagnitude > 0.0001f ? modelFwd.normalized : Vector3.forward;
			Quaternion yaw = Quaternion.FromToRotation(modelFwd, _d.toZombie);
			Vector3 bodyRight = Vector3.Cross(Vector3.up, _d.toZombie);
			Quaternion down = Quaternion.AngleAxis(-90f, bodyRight) * yaw;
			Vector3 ground = v.position - Origin.position;
			Vector3 lying = ground + Vector3.up * (HipsLift * scale) + _d.toZombie * (ChestOffset * scale);
			_d.hips.rotation = Quaternion.Slerp(Quaternion.identity, down, _k) * _d.hips.rotation;
			_d.hips.position = Vector3.Lerp(_d.hips.position, lying, _k);

			// arms up: hands above the shoulders (sky-ward, a little toward her feet), elbows out to the sides
			float buckle = Buckle * Mathf.Clamp01(1f - (Time.time - _d.lastImpact) / BuckleTime);
			float tremble = Mathf.Sin(Time.time * 23f) * 0.03f + Mathf.Sin(Time.time * 37f) * 0.02f;
			SolveArm(_d.left, bodyRight, _d.toZombie, buckle, tremble, _k);
			SolveArm(_d.right, bodyRight, _d.toZombie, buckle, -tremble, _k);
		}

		private static void SolveArm(Arm _a, Vector3 _bodyRight, Vector3 _toFeet, float _buckle, float _tremble, float _k)
		{
			if (_a == null)
			{
				return;
			}
			Vector3 s = _a.upper.position;
			float l1 = (_a.fore.position - s).magnitude;
			float l2 = (_a.hand.position - _a.fore.position).magnitude;
			float len = l1 + l2;
			Vector3 reach = Vector3.up * (ArmReach - _buckle + _tremble) + _toFeet * 0.25f - _bodyRight * (_a.side * 0.12f);
			Vector3 target = s + reach * len;
			Vector3 elbowDir = (_bodyRight * _a.side - Vector3.up * 0.3f).normalized;
			Vector3 animated = _a.hand.position;
			GtsMoveAnim.TwoBone(_a.upper, _a.fore, _a.hand, Vector3.Lerp(animated, target, _k), elbowDir);
			// palms up against the sole: the hand continues the forearm
			Vector3 foreDir = _a.hand.position - _a.fore.position;
			if (_a.hand.childCount > 0)
			{
				Vector3 handDir = _a.hand.GetChild(0).position - _a.hand.position;
				if (handDir.sqrMagnitude > 0.000001f && foreDir.sqrMagnitude > 0.000001f)
				{
					_a.hand.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(handDir, foreDir), _k) * _a.hand.rotation;
				}
			}
		}
	}
}
