using System;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace BannerlordBlocks
{
    internal sealed class PlacementPreview
    {
        private GameEntity entity;
        private MetaMesh visual;
        private Vec3 localCenter;

        internal void Show(Scene scene, Vec3 target, bool allowed)
        {
            bool created = entity == null;
            if (entity == null)
            {
                // Do not instantiate any prefab: it may retain physics despite createPhysics=false.
                // A copied visual resource on an empty entity has no physics definition at all.
                entity = GameEntity.CreateEmpty(scene, false, false, false);
                if (entity == null) throw new InvalidOperationException("Empty preview entity returned null");
                visual = MetaMesh.GetCopy("editor_cube", false, true);
                if (visual == null || visual.MeshCount == 0)
                    throw new InvalidOperationException("Preview editor_cube visual resource unavailable");
                entity.AddMultiMesh(visual, true);
                entity.RecomputeBoundingBox();
                entity.SetBodyFlags(BodyFlags.Disabled | BodyFlags.DoNotCollideWithRaycast |
                    BodyFlags.DontTransferToPhysicsEngine);
                entity.SetPhysicsState(false, true);
                BoundingBox bounds = entity.GetLocalBoundingBox();
                localCenter = (bounds.min + bounds.max) * 0.5f;
                if (Math.Abs(bounds.max.x - bounds.min.x - 1f) > 0.05f ||
                    Math.Abs(bounds.max.y - bounds.min.y - 1f) > 0.05f ||
                    Math.Abs(bounds.max.z - bounds.min.z - 1f) > 0.05f)
                    throw new InvalidOperationException("Preview visual is not a unit cube");
                entity.SetAlpha(0.3f);
                entity.SetReadyToRender(true);
                Log.Write("Preview created: physicsBody=" + entity.HasPhysicsBody() +
                    "; staticBody=" + entity.HasStaticPhysicsBody() + "; enabled=" + entity.GetPhysicsState() +
                    "; visualMeshes=" + visual.MeshCount + "; min=" + bounds.min + "; max=" + bounds.max);
                if (entity.HasPhysicsBody() || entity.HasStaticPhysicsBody())
                    throw new InvalidOperationException("Unexpected physics body on preview; disabling ghost for safety");
            }
            MatrixFrame frame = MatrixFrame.Identity;
            frame.origin = target - localCenter;
            entity.SetGlobalFrame(in frame, true);
            uint color = allowed ? 0xFF40E080u : 0xFFFF5050u;
            entity.SetFactorColor(color);
            for (int i = 0; i < visual.MeshCount; i++) visual.GetMeshAtIndex(i).Color = color;
            entity.SetContourColor(color, true);
            entity.SetVisibilityExcludeParents(true);
            if (created) Log.Write("Preview shown: target=" + target + "; allowed=" + allowed +
                "; visible=" + entity.GetVisibilityExcludeParents());
        }

        internal void Hide()
        {
            if (entity != null)
            {
                entity.SetContourColor(null, true);
                entity.SetVisibilityExcludeParents(false);
            }
        }

        internal void Remove()
        {
            if (entity == null) return;
            entity.Remove(0);
            entity = null;
            visual = null;
            Log.Write("Preview removed");
        }
    }
}
