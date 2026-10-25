using HarmonyLib;
using UnityEngine;

namespace GTS
{
	// Third person aims from the camera, pushed forward by the camera-to-player distance so the shot starts at the
	// player. CameraPatch pulls the camera in by the shrink scale, but that distance (and the look point, computed
	// from the unscaled camera) stayed full size, so the shot started metres past a tiny player - inside or behind
	// a zombie standing next to them. Rebuild the ray with the distances scaled to match the camera.
	[HarmonyPatch(typeof(EntityPlayerLocal), nameof(EntityPlayerLocal.GetLookRay))]
	public static class LookRayPatch
	{
		// Same mask vp_FPCamera.GetLookPoint uses (world geometry only).
		private const int LookPointMask = 1082195968;

		public static bool Prefix(EntityPlayerLocal __instance, ref Ray __result)
		{
			if (__instance.bFirstPersonView || !GtsConfig.IsShrunk(__instance) || __instance.vp_FPCamera == null)
			{
				return true;
			}
			float scale = GtsConfig.GetScale(__instance);
			Transform cam = __instance.playerCamera.transform;
			Ray ray = __instance.playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
			Vector3 origin = ray.origin + ray.direction * (__instance.vp_FPCamera.CurrentCameraDistance * scale);
			Vector3 lookPoint = GetLookPoint(__instance.vp_FPCamera, cam);
			if (Vector3.Distance(cam.position, lookPoint) < Vector3.Distance(cam.position, origin))
			{
				origin = lookPoint;
			}
			origin -= cam.forward * (0.15f * scale);
			ray.origin = origin + Origin.position;
			ray.direction += cam.up * 0.0001f;
			__result = ray;
			return false;
		}

		// vp_FPCamera.GetLookPoint, but from where the camera actually is after CameraPatch.
		private static Vector3 GetLookPoint(vp_FPCamera _fpCam, Transform _cam)
		{
			if (Physics.Linecast(_cam.position, _cam.position + _cam.forward * 1000f, out RaycastHit hit, LookPointMask)
				&& !hit.collider.isTrigger && _fpCam.Root.InverseTransformPoint(hit.point).z > 0f)
			{
				return hit.point;
			}
			return _cam.position + _cam.forward * 1000f;
		}
	}

	// Same problem for third person melee: the swing starts one full-size camera distance in front of the camera.
	[HarmonyPatch(typeof(EntityPlayerLocal), nameof(EntityPlayerLocal.GetMeleeRay))]
	public static class MeleeRayPatch
	{
		public static bool Prefix(EntityPlayerLocal __instance, ref Ray __result)
		{
			if (__instance.bFirstPersonView || __instance.LineOfSightObstructed || !GtsConfig.IsShrunk(__instance) || __instance.vp_FPCamera == null)
			{
				return true;
			}
			Transform cam = __instance.cameraTransform;
			float dist = __instance.vp_FPCamera.CurrentCameraDistance * GtsConfig.GetScale(__instance);
			__result = new Ray(cam.position + cam.forward * dist + Origin.position, cam.forward);
			return false;
		}
	}
}
