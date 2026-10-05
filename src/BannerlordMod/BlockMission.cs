using System;
using System.Collections.Generic;
using System.IO;
using BannerlordBlocks.Bridge;
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
        private BridgeClient bridge;
        private Replica replica;
        private readonly string bridgeSession = Guid.NewGuid().ToString("N");
        private readonly Dictionary<string, GameEntity> mirrored = new Dictionary<string, GameEntity>();
        private readonly Queue<Message> mirrorWork = new Queue<Message>();
        private string pendingRequest;
        private DateTime pendingSince;
        private bool bridgeConfigured;
        private bool BridgeReady { get { return replica != null && replica.Ready && mirrorWork.Count == 0 && pendingRequest == null; } }

        public override void OnMissionTick(float dt)
        {
            if (ended || failed) return;
            // Drain network data even while the battle is paused or the player is unavailable.
            try { PumpBridge(); }
            catch (Exception ex)
            {
                failed = true; diagnostics.Errors++;
                Log.Write("Bridge application failed: " + ex);
                bridge?.Dispose(); Clear();
                Notice("桥接状态应用失败，已停用本场景；请查看日志。");
                return;
            }
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
                    ConfigureBridge();
                    Notice((bridgeConfigured ? "桥接模式：等待同步；" : "建造已就绪：") + "Ins 放置，Ctrl+Ins 开关预览；绿=可放，红=不可放；PgUp 选择，Del 删除。测试 " + diagnostics.Id);
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
                        if (bridgeConfigured)
                        {
                            if (RequireBridge())
                            {
                                BoundingBox bounds = selected.GetGlobalBoundingBox();
                                SendRequest("BreakBlockRequest", ToCell((bounds.min + bounds.max) * 0.5f));
                            }
                        }
                        else RemoveBlock(selected);
                    }
                }
                if (Input.IsKeyPressed(InputKey.PageDown))
                {
                    if (bridgeConfigured) { if (RequireBridge()) SendRequest("ClearRequest", null); }
                    else { Clear(); Notice("方块已清空。"); }
                }
                UpdatePreview(dt);
            }
            catch (Exception ex)
            {
                failed = true;
                diagnostics.Errors++;
                Log.Write("Disabled after engine error: " + ex);
                Notice("方块原型：发生错误，已停用本场景操作，请查看日志。");
                bridge?.Dispose();
                Clear();
            }
        }

        private void Place()
        {
            if (bridgeConfigured && !RequireBridge()) return;
            Agent player = Mission.MainAgent;
            Vec3 target;
            if (!TryPlacementTarget(player, out target)) return;
            if (!ValidatePlacementTarget(target, true)) return;
            if (bridgeConfigured)
            {
                // Establish a fixed scene origin before serializing integer cells.
                if (!gridBaseHeight.HasValue) gridBaseHeight = target.z - 0.5f;
                SendRequest("PlaceBlockRequest", ToCell(target));
            }
            else CreateBlock(target);
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
            if (bridgeConfigured) Notice("M2 桥接：" + (BridgeReady ? "已同步" : "断线/同步中/等待确认") +
                "；序号=" + (replica == null ? 0 : replica.Sequence));
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
                allowed = allowed && ValidatePlacementTarget(target, false) && (!bridgeConfigured || BridgeReady);
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
            bridge?.Dispose();
            mirrorWork.Clear(); mirrored.Clear();
            Clear();
            if (!summaryWritten)
            {
                summaryWritten = true;
                Log.Write("Test end: reason=" + reason + "; " + diagnostics.Summary(blocks.Count, Mission == null ? 0 : Mission.Agents.Count));
            }
        }

        protected override void OnEndMission() { Finish("mission end"); }
        public override void OnRemoveBehavior() { Finish("behavior removed"); }

        private void ConfigureBridge()
        {
            string path = System.IO.Path.Combine(Log.DirectoryPath, "bridge-port.txt");
            if (!File.Exists(path)) return; // Existing M1 remains the default.
            bridgeConfigured = true;
            int port;
            if (!int.TryParse(File.ReadAllText(path).Trim(), out port) || port < 1024 || port > 65535)
            { Notice("bridge-port.txt 无效；本场景禁止建造，不回退到本地权威。"); return; }
            replica = new Replica(bridgeSession, Mission.SceneName);
            bridge = new BridgeClient(bridgeSession, Mission.SceneName, port);
            Log.Write("M2 bridge enabled; session=" + bridgeSession + "; port=" + port);
            Notice("M2 桥接已启用，正在连接本机模拟服务。");
        }

        private bool RequireBridge()
        {
            if (BridgeReady) return true;
            Notice("桥接未就绪或正在等待确认，暂不能修改方块。"); return false;
        }

        private Cell ToCell(Vec3 target)
        {
            return new Cell { X = (int)Math.Floor(target.x), Y = (int)Math.Floor(target.y),
                Z = (int)Math.Round(target.z - 0.5f - gridBaseHeight.Value) };
        }

        private void SendRequest(string type, Cell cell)
        {
            var message = Message.For(type, bridgeSession, Mission.SceneName);
            message.Request = Guid.NewGuid().ToString("N"); message.Cell = cell; message.BaseHeight = gridBaseHeight;
            if (!bridge.Send(message)) { Notice("桥接发送队列已满，请稍后再试。"); return; }
            pendingRequest = message.Request; pendingSince = DateTime.UtcNow;
            Log.Write("M2 request: " + type + "; request=" + pendingRequest);
        }

        private void PumpBridge()
        {
            if (bridge == null) return;
            BridgeEvent item;
            for (int i = 0; i < 16 && bridge.TryRead(out item); i++)
            {
                if (item.Status != null)
                {
                    replica.Disconnect(); pendingRequest = null;
                    // Retain the last mirror, but do not apply queued work until a fresh snapshot arrives.
                    mirrorWork.Clear();
                    Log.Write("M2 " + item.Status + "; session=" + bridgeSession);
                    Notice("M2 桥接断线，暂停建造；将自动重连并同步。");
                    continue;
                }
                Message message = item.Message;
                if (message.Session != bridgeSession || message.Scene != Mission.SceneName) continue;
                if (message.Type == "Result" && message.Request == pendingRequest)
                {
                    pendingRequest = null;
                    if (message.Error != null) Notice("模拟服务拒绝请求：" + message.Error);
                }
                if (message.Type != "Snapshot" && message.Type != "BlockDelta") continue;
                bool wasReady = replica.Ready;
                if (!replica.Apply(message))
                {
                    if (wasReady && !replica.Ready) RequestSnapshot();
                    continue;
                }
                // Adopt the authority's origin even when the first restored change is a delta.
                gridBaseHeight = replica.BaseHeight.HasValue ? (float?)replica.BaseHeight.Value : null;
                if (message.Type == "Snapshot")
                {
                    mirrorWork.Clear(); pendingRequest = null;
                    foreach (string key in mirrored.Keys)
                        mirrorWork.Enqueue(new Message { Type = "RemoveMirror", Request = key });
                    foreach (Cell cell in replica.Blocks.Values)
                        mirrorWork.Enqueue(new Message { Type = "AddMirror", Cell = cell, BaseHeight = replica.BaseHeight, Present = true });
                    Log.Write("M2 snapshot; sequence=" + replica.Sequence + "; blocks=" + replica.Blocks.Count);
                }
                else mirrorWork.Enqueue(message);
            }
            if (pendingRequest != null && (DateTime.UtcNow - pendingSince).TotalSeconds > 5)
            { pendingRequest = null; RequestSnapshot(); }
            // A per-frame engine work budget; network threads never create/remove entities.
            DateTime start = DateTime.UtcNow;
            for (int i = 0; replica.Ready && i < 32 && mirrorWork.Count > 0; i++)
            {
                Message work = mirrorWork.Dequeue();
                string key = work.Type == "RemoveMirror" ? work.Request : work.Cell.Key;
                GameEntity entity;
                if (mirrored.TryGetValue(key, out entity))
                { RemoveBlock(entity); mirrored.Remove(key); }
                if (work.Present)
                {
                    Cell cell = work.Cell;
                    Vec3 position = new Vec3(cell.X + 0.5f, cell.Y + 0.5f, (float)work.BaseHeight.Value + cell.Z + 0.5f);
                    CreateBlock(position); mirrored.Add(key, blocks[blocks.Count - 1]);
                }
                if ((DateTime.UtcNow - start).TotalMilliseconds >= 4) break;
            }
        }

        private void RequestSnapshot()
        {
            replica.Disconnect(); mirrorWork.Clear();
            if (!bridge.Send(Message.For("SnapshotRequest", bridgeSession, Mission.SceneName)))
            { bridge.Dispose(); failed = true; Notice("桥接重同步请求失败，已停用本场景。"); }
        }

        private void RemoveBlock(GameEntity entity)
        {
            if (selected == entity) SetSelection(null);
            entity.Remove(0); blocks.Remove(entity); diagnostics.Deleted++;
            Log.Write("Deleted; session=" + diagnostics.Id + "; count=" + blocks.Count);
        }
    }
}
