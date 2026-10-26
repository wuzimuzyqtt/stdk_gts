using UnityEngine;

namespace GTS
{
	// Server -> clients: "this player was just squashed here", play the gore locally and hide the body.
	public class NetPackageGtsFx : NetPackage
	{
		private int entityId;
		private Vector3 pos;
		private float scale;   // victim shrink scale; the gore is sized to match

		public override NetPackageDirection PackageDirection => NetPackageDirection.ToClient;

		public NetPackageGtsFx Setup(int _entityId, Vector3 _pos, float _scale)
		{
			entityId = _entityId;
			pos = _pos;
			scale = _scale;
			return this;
		}

		public override void read(PooledBinaryReader _br)
		{
			entityId = _br.ReadInt32();
			pos = new Vector3(_br.ReadSingle(), _br.ReadSingle(), _br.ReadSingle());
			scale = _br.ReadSingle();
		}

		public override void write(PooledBinaryWriter _bw)
		{
			base.write(_bw);
			_bw.Write(entityId);
			_bw.Write(pos.x);
			_bw.Write(pos.y);
			_bw.Write(pos.z);
			_bw.Write(scale);
		}

		public override void ProcessPackage(World _world, GameManager _callbacks)
		{
			if (_world != null)
			{
				PlayLocal(entityId, pos, scale);
			}
		}

		public override int GetLength()
		{
			return 20;
		}

		public static void PlayLocal(int _entityId, Vector3 _pos, float _scale)
		{
			GtsDriver.Squashed[_entityId] = Time.time;
			if (GameManager.IsDedicatedServer)
			{
				return;
			}
			// everything is laid on the actual ground under the victim, not their (possibly sunk-in) position
			Vector3 ground = GtsGround.Find(_pos, out _);
			GtsGore.Play(ground, _scale);
			GtsGore.Splat(ground);
			// must run before GtsDriver hides the squashed body (next LateUpdate), while its pose is still there
			World world = GameManager.Instance.World;
			GtsCorpse.Spawn(world != null ? world.GetEntity(_entityId) as EntityAlive : null, ground, _scale);
		}
	}
}
