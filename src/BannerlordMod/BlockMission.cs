using System;
using System.Collections.Generic;
using System.IO;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace BannerlordBlocks
{
    // Engine access is confined to mission callbacks on the game thread.
    internal sealed class BlockMission : MissionLogic
    {
        private readonly List<GameEntity> blocks = new List<GameEntity>();
        private readonly TestDiagnostics diagnostics = new TestDiagnostics();
        private bool summaryWritten;
        private string prefab;
        private bool initialized, failed, ended;
        private GameEntity selected;
        private float? gridBaseHeight;
        private readonly PlacementPreview preview = new PlacementPreview();
        private bool previewEnabled = true, previewFailed;
        private float previewElapsed;
        private const float Reach = 8f;

        public override void OnMissionTick(float dt)
        {
            if (ended || failed) return;
            if (Mission.Mode != MissionMode.Battle || Mission.MainAgent == null)
            {
                HidePreview();
                return;
            }
            try
            {
                if (diagnostics.Sample(dt))
                    Log.Write("Sample: " + diagnostics.Summary(blocks.Count, Mission.Agents.Count));
                if (!initialized)
                {
                    initialized = true;
                    Log.Write("Test begin: session=" + diagnostics.Id + "; scene=" + Mission.SceneName);
                    string path = System.IO.Path.Combine(Log.DirectoryPath, "prefab.txt");
                    // Confirmed in Native/Prefabs/editor_contents.xml; runtime availability is still tested.
                    prefab = File.Exists(path) ? File.ReadAllText(path).Trim() : "editor_cube";
                    if (prefab.Length == 0 || !GameEntity.PrefabExists(prefab))
                    {
                        failed = true;
                        Log.Write("BLOCKED: prefab unavailable. Configure prefab.txt with a cube prefab including physics.");
                        Notice("方块原型：资源不可用，请查看日志。");
                        return;
                    }
                    Log.Write("Ready: Insert place at aimed terrain/block face; reach=" + Reach + "; no block count cap; Prefab=" + prefab);
                    Notice("建造已就绪：Ins 放置，Ctrl+Ins 开关预览；绿=可放，红=不可放；PgUp 选择，Del 删除。测试 " + diagnostics.Id);
                }
                if (Input.IsKeyPressed(InputKey.Insert))
                {
                    if (ControlDown())
                    {
                        previewEnabled = !previewEnabled;
                        HidePreview();
                        Notice("放置预览：" + (previewEnabled ? "开启" : "关闭"));
                    }
                    else { Log.Write("Insert received"); Place(); }
                }
                if (Input.IsKeyPressed(InputKey.PageUp))
                {
                    if (ControlDown()) ShowStatus();
                    else Select();
                }
                if (Input.IsKeyPressed(InputKey.Delete))
                {
                    if (selected == null) Notice("尚未选中方块：请对准方块按 PgUp。");
                    else
                    {
                        GameEntity removing = selected;
                        SetSelection(null);
                        removing.Remove(0);
                        blocks.Remove(removing);
                        diagnostics.Deleted++;
                        Log.Write("Deleted; session=" + diagnostics.Id + "; count=" + blocks.Count);
                        Notice("已删除方块，剩余 " + blocks.Count);
                    }
                }
                if (Input.IsKeyPressed(InputKey.PageDown)) { Clear(); Notice("方块已清空。"); }
                UpdatePreview(dt);
            }
            catch (Exception ex)
            {
                failed = true;
                diagnostics.Errors++;
                Log.Write("Disabled after engine error: " + ex);
                Notice("方块原型：发生错误，已停用本场景操作，请查看日志。");
                Clear();
            }
        }

        private void Place()
        {
            Agent player = Mission.MainAgent;
            Vec3 target;
            if (!TryPlacementTarget(player, out target)) return;
            if (!ValidatePlacementTarget(target, true)) return;
            CreateBlock(target);
        }

        private bool ValidatePlacementTarget(Vec3 target, bool feedback)
        {
            if (!Finite(target.x) || !Finite(target.y) || !Finite(target.z) || !Mission.IsPositionInsideBoundaries(target.AsVec2))
            { Reject("ground or boundary", "放置位置无有效地面或超出边界。", feedback); return false; }
            // Conservative exclusion envelopes, including riderless horses and all agents.
            foreach (Agent agent in Mission.Agents)
            {
                Vec3 p = agent.Position;
                float radius = agent.IsMount ? 1.8f : 1.0f;
                if (Math.Abs(target.x - p.x) < radius && Math.Abs(target.y - p.y) < radius &&
                    target.z + 0.5f > p.z - 0.5f && target.z - 0.5f < p.z + 2.5f)
                { Reject("agent space; mount=" + agent.IsMount, "放置位置与角色或马匹重叠，请移到空旷处。", feedback); return false; }
            }
            foreach (GameEntity block in blocks)
            {
                BoundingBox bounds = block.GetGlobalBoundingBox();
                Vec3 p = (bounds.min + bounds.max) * 0.5f;
                if (Math.Abs(p.x - target.x) < 0.99f && Math.Abs(p.y - target.y) < 0.99f && Math.Abs(p.z - target.z) < 0.99f)
                { Reject("occupied", "放置位置已被方块占用。", feedback); return false; }
            }
            return true;
        }

        private void CreateBlock(Vec3 target)
        {
            MatrixFrame frame = MatrixFrame.Identity;
            frame.origin = target;
            GameEntity entity = GameEntity.Instantiate(Mission.Scene, prefab, frame, false);
            if (entity == null) throw new InvalidOperationException("Prefab instantiation returned null");
            // Track before diagnostics so exceptions also clean up the newly created entity.
            blocks.Add(entity);
            BoundingBox localBounds = entity.GetLocalBoundingBox();
            Vec3 min = localBounds.min;
            Vec3 max = localBounds.max;
            Log.Write("Prefab bounds min=" + min + "; max=" + max);
            float width = max.x - min.x, depth = max.y - min.y, height = max.z - min.z;
            if (!UnitDimension(width) || !UnitDimension(depth) || !UnitDimension(height))
                throw new InvalidOperationException("Resource is not a unit cube: " + width + ", " + depth + ", " + height);
            // Allow an off-center resource origin without assuming it is centered.
            Vec3 center = (min + max) * 0.5f;
            frame.origin = target - center;
            entity.SetGlobalFrame(in frame, true);
            LogPhysics(entity, "Before activation");
            // A prefab physics definition / bounding box does not guarantee an active scene body.
            // Set entity ownership explicitly; do not set ray-only or collision-exclusion flags.
            entity.SetBodyFlags(BodyFlags.BodyOwnerEntity | BodyFlags.NotDestructible);
            if (!entity.HasPhysicsBody() && !entity.HasStaticPhysicsBody())
            {
                // Use the prefab's own shape/material rather than inventing a mesh collider.
                PhysicsShape shape = entity.GetBodyShape();
                if (shape == null) throw new InvalidOperationException("Prefab has no physics shape");
                PhysicsMaterial material = entity.GetPhysicsMaterial();
                if (!material.IsValid) throw new InvalidOperationException("Prefab has no valid physics material");
                entity.AddPhysics(1f, center, shape, Vec3.Zero, Vec3.Zero, material, true, 0);
                Log.Write("Added static physics body; collisionGroupID=0 (runtime behavior pending verification)");
            }
            entity.SetPhysicsState(true, true);
            LogPhysics(entity, "After activation");
            var physicsBounds = entity.ComputeGlobalPhysicsBoundingBoxMinMax();
            Log.Write("Physics bounds min=" + physicsBounds.Item1 + "; max=" + physicsBounds.Item2);
            Log.Write("Created at " + target + "; count=" + blocks.Count + "; body=" + entity.BodyFlag);
            diagnostics.Created++;
            if (!gridBaseHeight.HasValue)
            {
                gridBaseHeight = target.z - 0.5f;
                Log.Write("Grid anchored: baseHeight=" + gridBaseHeight.Value + "; session=" + diagnostics.Id);
            }
            diagnostics.PeakBlocks = Math.Max(diagnostics.PeakBlocks, blocks.Count);
            Notice("方块已生成，数量：" + blocks.Count);
        }

        private bool TryPlacementTarget(Agent player, out Vec3 target, bool feedback = true)
        {
            target = new Vec3(float.NaN, float.NaN, float.NaN);
            Vec3 start = player.GetEyeGlobalPosition();
            Vec3 end = start + player.LookDirection * Reach;
            float distance;
            Vec3 point;
            WeakGameEntity hit;
            if (!Mission.Scene.RayCastForClosestEntityOrTerrain(start, end, out distance, out point,
                out hit, 0.001f, BodyFlags.CommonFocusRayCastExcludeFlags))
            { Reject("aim missed", "8 米内未命中：请瞄准附近地面或已有方块。", feedback); return false; }
            if (!Finite(point.x) || !Finite(point.y) || !Finite(point.z))
            { Reject("invalid hit", "命中位置无效。", feedback); return false; }

            foreach (GameEntity block in blocks)
            {
                if (block != hit) continue;
                BoundingBox bounds = block.GetGlobalBoundingBox();
                Vec3 center = (bounds.min + bounds.max) * 0.5f;
                int axis, sign;
                PlacementGeometry.FaceOffset(point.x - center.x, point.y - center.y,
                    point.z - center.z, out axis, out sign);
                Vec3 offset = axis == 0 ? new Vec3(sign, 0, 0) :
                    axis == 1 ? new Vec3(0, sign, 0) : new Vec3(0, 0, sign);
                target = center + offset;
                if (feedback) Log.Write("Placement target: block face; axis=" + axis + "; sign=" + sign +
                    "; distance=" + distance + "; session=" + diagnostics.Id);
                // Do not build below ground. Small tolerance accommodates ray/bounds precision.
                float ground = TerrainHeight(target);
                if (!Finite(ground) || target.z - 0.5f < ground - 0.05f)
                { Reject("below ground", "目标方块低于地面，无法放置。", feedback); return false; }
                return true;
            }

            if (hit.IsValid && (hit.BodyFlag & BodyFlags.BodyOwnerTerrain) == 0)
            { Reject("host object", "当前只支持地面和本项目方块，不在原版场景物体上建造。", feedback); return false; }
            target = new Vec3(PlacementGeometry.CellCenter(point.x), PlacementGeometry.CellCenter(point.y), point.z);
            float terrain = TerrainHeight(target);
            if (!Finite(terrain))
            { target.z = float.NaN; Reject("invalid terrain", "目标地面高度无效。", feedback); return false; }
            target.z = PlacementGeometry.GroundCenter(terrain, gridBaseHeight ?? terrain);
            if (feedback) Log.Write("Placement target: terrain; distance=" + distance + "; session=" + diagnostics.Id);
            return true;
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private float TerrainHeight(Vec3 position)
        {
            // Existing blocks must not masquerade as terrain and shift the grid upwards.
            return Mission.Scene.GetGroundHeightAtPosition(position,
                BodyFlags.CommonCollisionExcludeFlags | BodyFlags.BodyOwnerEntity);
        }

        private void Reject(string reason, string message, bool feedback = true)
        {
            if (!feedback) return;
            diagnostics.Rejected++;
            Log.Write("Rejected: " + reason + "; session=" + diagnostics.Id);
            Notice(message);
        }

        private void ShowStatus()
        {
            Log.Write("Manual status: " + diagnostics.Summary(blocks.Count, Mission.Agents.Count));
            Notice("测试 " + diagnostics.Id + "：方块 " + blocks.Count + "（无数量上限）" +
                "，Agent " + Mission.Agents.Count + "，拒绝 " + diagnostics.Rejected +
                " 次；选中=" + (selected != null) + "；网格基准=" +
                (gridBaseHeight.HasValue ? gridBaseHeight.Value.ToString("F3") : "未建立"));
            Log.Write("Preview status: enabled=" + previewEnabled + "; failed=" + previewFailed + "; session=" + diagnostics.Id);
            if (selected != null) LogPhysics(selected, "Selected block");
        }

        private void SetSelection(GameEntity entity)
        {
            TryHighlight(selected, null);
            selected = entity;
            TryHighlight(selected, 0xFFFFCC00u);
        }

        private void TryHighlight(GameEntity entity, uint? color)
        {
            if (entity == null) return;
            try { entity.SetContourColor(color, true); }
            catch (Exception ex)
            {
                diagnostics.Errors++;
                Log.Write("Optional highlight unavailable; session=" + diagnostics.Id + "; " + ex);
            }
        }

        private static void Notice(string text)
        {
            InformationManager.DisplayMessage(new InformationMessage(text));
        }

        private static void LogPhysics(GameEntity entity, string phase)
        {
            Log.Write(phase + ": hasBody=" + entity.HasBody() + "; physicsBody=" + entity.HasPhysicsBody() +
                "; staticBody=" + entity.HasStaticPhysicsBody() + "; enabled=" + entity.GetPhysicsState() +
                "; triangles=" + entity.GetPhysicsTriangleCount() + "; flags=" + entity.BodyFlag +
                "; descFlags=" + entity.PhysicsDescBodyFlag + "; mobility=" + entity.GetMobility());
        }

        private static bool UnitDimension(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value - 1f) <= 0.05f;
        }

        private void Select()
        {
            SetSelection(null);
            Vec3 start = Mission.MainAgent.GetEyeGlobalPosition();
            Vec3 end = start + Mission.MainAgent.LookDirection * Reach;
            float distance;
            WeakGameEntity hit;
            if (Mission.Scene.RayCastForClosestEntityOrTerrain(start, end, out distance, out hit, 0.01f,
                BodyFlags.CommonFocusRayCastExcludeFlags))
            {
                GameEntity entity = GameEntity.CreateFromWeakEntity(hit);
                foreach (GameEntity block in blocks)
                    if (block == entity) { SetSelection(block); diagnostics.Selected++; break; }
                Log.Write("Native ray hit; distance=" + distance + "; owned block=" + (selected != null));
                Notice(selected != null ? "已选中方块，按 Del 删除。" : "射线未命中本项目方块。");
            }
            else { Log.Write("Native ray missed"); Notice("射线未命中，请对准方块。"); }
        }

        private void Clear()
        {
            RemovePreview();
            // Do not allow a failed highlight reset to prevent entity cleanup.
            try { SetSelection(null); }
            catch (Exception ex) { selected = null; diagnostics.Errors++; Log.Write("Highlight cleanup failed: " + ex); }
            for (int i = blocks.Count - 1; i >= 0; i--)
            {
                try { blocks[i].Remove(0); blocks.RemoveAt(i); diagnostics.Deleted++; }
                catch (Exception ex) { diagnostics.Errors++; Log.Write("Cleanup failed: " + ex); }
            }
            Log.Write("Cleanup remaining=" + blocks.Count + "; session=" + diagnostics.Id);
        }

        private static bool ControlDown()
        {
            return Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl);
        }

        private void UpdatePreview(float dt)
        {
            if (!previewEnabled || previewFailed) return;
            previewElapsed += dt;
            if (previewElapsed < 0.05f) return; // At most 20 queries per game second.
            previewElapsed = 0;
            try
            {
                Vec3 target;
                bool allowed = TryPlacementTarget(Mission.MainAgent, out target, false);
                if (!Finite(target.x) || !Finite(target.y) || !Finite(target.z)) { preview.Hide(); return; }
                allowed = allowed && ValidatePlacementTarget(target, false);
                preview.Show(Mission.Scene, target, allowed);
            }
            catch (Exception ex)
            {
                previewFailed = true;
                diagnostics.Errors++;
                Log.Write("Preview disabled (building remains available): " + ex);
                Notice("预览不可用，已关闭预览；仍可按 Ins 放置。");
                RemovePreview();
            }
        }

        private void HidePreview()
        {
            try { preview.Hide(); }
            catch (Exception ex) { previewFailed = true; Log.Write("Preview hide failed: " + ex); RemovePreview(); }
        }

        private void RemovePreview()
        {
            try { preview.Remove(); }
            catch (Exception ex) { diagnostics.Errors++; Log.Write("Preview cleanup failed: " + ex); }
        }

        private void Finish(string reason)
        {
            ended = true;
            Clear();
            if (!summaryWritten)
            {
                summaryWritten = true;
                Log.Write("Test end: reason=" + reason + "; " + diagnostics.Summary(blocks.Count, Mission == null ? 0 : Mission.Agents.Count));
            }
        }

        protected override void OnEndMission() { Finish("mission end"); }
        public override void OnRemoveBehavior() { Finish("behavior removed"); }
    }
}
