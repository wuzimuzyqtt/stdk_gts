using System;
using UnityEngine;

namespace GTS
{
	public static class GtsDebug
	{
		// Layer mask ItemActionRanged.fireShot passes to Voxel.Raycast.
		private const int ShotLayerMask = -538751005;

		// Lists every collider along the shot ray (the real shot only reports the first one)
		// and any collider the ray starts inside, which Physics.Raycast silently skips.
		public static void TraceShot(EntityPlayerLocal _player, Ray _ray, Action<string> _out)
		{
			Vector3 origin = _ray.origin - Origin.position;
			Vector3 cam = _player.cameraTransform.position + Origin.position;
			_out("ray origin " + _ray.origin.ToString("F3") + " dir " + _ray.direction.ToString("F2")
				+ " | camera " + cam.ToString("F3") + " | feet " + _player.position.ToString("F3")
				+ " | origin-camera " + (_ray.origin - cam).magnitude.ToString("F3") + "m");
			foreach (Collider c in Physics.OverlapSphere(origin, 0.01f, ShotLayerMask))
			{
				_out("  origin is INSIDE " + Describe(c) + " (skipped by the shot)");
			}
			RaycastHit[] hits = Physics.RaycastAll(origin, _ray.direction, 50f, ShotLayerMask);
			Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
			if (hits.Length == 0)
			{
				_out("  nothing hit within 50m");
			}
			for (int i = 0; i < hits.Length && i < 8; i++)
			{
				_out("  " + (i == 0 ? "FIRST " : "") + hits[i].distance.ToString("F2") + "m  " + Describe(hits[i].collider));
			}
		}

		private static string Describe(Collider _c)
		{
			Entity e = _c.GetComponentInParent<Entity>();
			return "'" + _c.name + "' tag=" + _c.tag + " layer=" + _c.gameObject.layer + " enabled=" + _c.enabled
				+ (e != null ? " entity=" + e.GetDebugName() + "#" + e.entityId : "");
		}
	}
}
