using System.Collections.Generic;
using UnityEngine;

namespace GTS
{
	// Procedural stomp: zombies have no stomp animation, so after the Animator has posed the zombie each frame we
	// bend its right leg on top of that pose - raise the knee, hold, slam the foot down, ease back out.
	// Purely visual and local; the server decides when a stomp starts (NetPackageGtsStomp) and whether it hits.
	public static class GtsStompAnim
	{
		private class Stomp
		{
			public EntityAlive zombie;
			public Transform thigh, shin, foot, spine;
			public float start;
		}

		private static readonly Dictionary<int, Stomp> active = new Dictionary<int, Stomp>();
		private static readonly List<int> finished = new List<int>();

		public static void Play(EntityAlive _zombie)
		{
			Transform model = _zombie != null && _zombie.emodel != null ? _zombie.emodel.GetModelTransform() : null;
			if (model == null)
			{
				return;
			}
			Stomp s = new Stomp
			{
				zombie = _zombie,
				thigh = model.FindInChilds("RightUpLeg"),
				shin = model.FindInChilds("RightLeg"),
				foot = model.FindInChilds("RightFoot"),
				spine = model.FindInChilds("Spine1") ?? model.FindInChilds("Spine"),
				start = Time.time
			};
			if (s.thigh == null || s.shin == null)
			{
				Log.Warning("[GTS] stomp: leg bones not found on " + _zombie.GetDebugName());
				return;
			}
			active[_zombie.entityId] = s;
		}

		// Called from GtsDriver.LateUpdate, i.e. after this frame's animation has been applied.
		public static void LateUpdate()
		{
			if (active.Count == 0)
			{
				return;
			}
			foreach (KeyValuePair<int, Stomp> kv in active)
			{
				Stomp s = kv.Value;
				float t = Time.time - s.start;
				if (s.zombie == null || s.zombie.IsDead() || s.thigh == null || t >= GtsConfig.StompAnimLength)
				{
					finished.Add(kv.Key);
					continue;
				}
				Pose(s, t);
			}
			foreach (int id in finished)
			{
				active.Remove(id);
			}
			finished.Clear();
		}

		// Angles in degrees at time t: raise -> hold -> slam forward -> planted -> recover.
		private static void Pose(Stomp _s, float _t)
		{
			float raiseEnd = GtsConfig.StompRaiseTime;
			float holdEnd = raiseEnd + GtsConfig.StompHoldTime;
			float slamEnd = holdEnd + GtsConfig.StompSlamTime;
			float plantEnd = slamEnd + GtsConfig.StompPlantTime;
			float hip, knee, lean, weight = 1f;
			if (_t < raiseEnd)
			{
				float p = EaseOut(_t / raiseEnd);
				hip = GtsConfig.StompHipAngle * p;
				knee = GtsConfig.StompKneeAngle * p;
				lean = -GtsConfig.StompLeanBack * p;
			}
			else if (_t < holdEnd)
			{
				hip = GtsConfig.StompHipAngle;
				knee = GtsConfig.StompKneeAngle;
				lean = -GtsConfig.StompLeanBack;
			}
			else
			{
				// drive the foot down and forward: the knee straightens while the thigh only drops to a forward angle
				float p = EaseIn((_t - holdEnd) / GtsConfig.StompSlamTime);
				hip = Mathf.Lerp(GtsConfig.StompHipAngle, GtsConfig.StompSlamHipAngle, p);
				knee = Mathf.Lerp(GtsConfig.StompKneeAngle, 0f, p);
				lean = Mathf.Lerp(-GtsConfig.StompLeanBack, GtsConfig.StompLeanForward, p);
				if (_t >= plantEnd)
				{
					weight = 1f - Mathf.Clamp01((_t - plantEnd) / (GtsConfig.StompAnimLength - plantEnd));
				}
			}
			hip *= weight;
			knee *= weight;
			lean *= weight;

			// Bend around the zombie's own right axis, in world space, so it works whatever the bones' local axes are.
			float sign = GtsConfig.StompAxisSign;
			Vector3 right = _s.zombie.transform.right;
			if (_s.spine != null)
			{
				_s.spine.rotation = Quaternion.AngleAxis(-lean * sign, right) * _s.spine.rotation;
			}
			_s.thigh.rotation = Quaternion.AngleAxis(-hip * sign, right) * _s.thigh.rotation;
			_s.shin.rotation = Quaternion.AngleAxis(knee * sign, right) * _s.shin.rotation;
			if (_s.foot != null)
			{
				// keep the sole level so it reads as a stomp and not a kick
				_s.foot.rotation = Quaternion.AngleAxis((hip - knee) * sign, right) * _s.foot.rotation;
			}
		}

		private static float EaseOut(float _x)
		{
			_x = Mathf.Clamp01(_x);
			return 1f - (1f - _x) * (1f - _x);
		}

		private static float EaseIn(float _x)
		{
			_x = Mathf.Clamp01(_x);
			return _x * _x;
		}
	}

	// Server -> clients: this zombie starts a stomp now.
	public class NetPackageGtsStomp : NetPackage
	{
		private int entityId;

		public override NetPackageDirection PackageDirection => NetPackageDirection.ToClient;

		public NetPackageGtsStomp Setup(int _entityId)
		{
			entityId = _entityId;
			return this;
		}

		public override void read(PooledBinaryReader _br)
		{
			entityId = _br.ReadInt32();
		}

		public override void write(PooledBinaryWriter _bw)
		{
			base.write(_bw);
			_bw.Write(entityId);
		}

		public override void ProcessPackage(World _world, GameManager _callbacks)
		{
			if (_world != null)
			{
				GtsStompAnim.Play(_world.GetEntity(entityId) as EntityAlive);
			}
		}

		public override int GetLength()
		{
			return 4;
		}
	}
}
