using UnityEngine;

namespace GTS
{
	public static class GtsGround
	{
		private static readonly RaycastHit[] hits = new RaycastHit[16];

		// World-space ground point below _worldPos: the first terrain or block surface hit by a ray coming down from
		// a metre above, ignoring players, zombies, gore and anything else that is not world geometry.
		// Falls back to _worldPos itself.
		public static Vector3 Find(Vector3 _worldPos, out Vector3 _normal)
		{
			_normal = Vector3.up;
			Vector3 from = _worldPos + Vector3.up - Origin.position;
			int n = Physics.RaycastNonAlloc(from, Vector3.down, hits, 3f, ~0, QueryTriggerInteraction.Ignore);
			float best = float.MaxValue;
			Vector3 ground = _worldPos;
			for (int i = 0; i < n; i++)
			{
				if (hits[i].distance < best && GameUtils.IsBlockOrTerrain(hits[i].collider))
				{
					best = hits[i].distance;
					ground = hits[i].point + Origin.position;
					_normal = hits[i].normal;
				}
			}
			return ground;
		}

		// Moves _go up or down so the lowest point of what it draws sits _lift above _groundY (world space, without
		// Origin). Block decoration models are not pivoted at their underside, so placing the pivot is not enough.
		public static void SitOnGround(GameObject _go, float _groundY, float _lift)
		{
			Renderer[] renderers = _go.GetComponentsInChildren<Renderer>();
			if (renderers.Length == 0)
			{
				return;
			}
			float minY = float.MaxValue;
			foreach (Renderer r in renderers)
			{
				minY = Mathf.Min(minY, r.bounds.min.y);
			}
			_go.transform.position += Vector3.up * (_groundY - Origin.position.y + _lift - minY);
		}
	}
}
