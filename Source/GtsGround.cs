using UnityEngine;

namespace GTS
{
	public static class GtsGround
	{
		private static readonly RaycastHit[] hits = new RaycastHit[16];

		// World-space ground point under _worldPos: the terrain or block surface the victim is standing on, found by
		// a ray coming down from a metre above, ignoring players, zombies, gore and anything else that is not world
		// geometry. Surfaces more than StandTolerance above the feet (a table or half block the tiny victim is under)
		// are skipped; of the rest the highest wins. Falls back to the nearest surface, then to _worldPos itself.
		private const float StandTolerance = 0.25f;

		public static Vector3 Find(Vector3 _worldPos, out Vector3 _normal)
		{
			_normal = Vector3.up;
			Vector3 from = _worldPos + Vector3.up - Origin.position;
			int n = Physics.RaycastNonAlloc(from, Vector3.down, hits, 3f, ~0, QueryTriggerInteraction.Ignore);
			float best = float.MaxValue, nearest = float.MaxValue;
			Vector3 ground = _worldPos, fallback = _worldPos;
			Vector3 fallbackNormal = Vector3.up;
			bool found = false;
			for (int i = 0; i < n; i++)
			{
				if (!GameUtils.IsBlockOrTerrain(hits[i].collider))
				{
					continue;
				}
				Vector3 p = hits[i].point + Origin.position;
				float above = p.y - _worldPos.y;
				if (above <= StandTolerance && hits[i].distance < best)
				{
					best = hits[i].distance;
					ground = p;
					_normal = hits[i].normal;
					found = true;
				}
				if (Mathf.Abs(above) < nearest)
				{
					nearest = Mathf.Abs(above);
					fallback = p;
					fallbackNormal = hits[i].normal;
				}
			}
			if (!found && nearest < float.MaxValue)
			{
				ground = fallback;
				_normal = fallbackNormal;
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
