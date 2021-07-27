﻿using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.Utils;

namespace TAS.EverestInterop.Hitboxes {
    public static class HitboxRoomBoundary {
        public static void Load() {
            On.Monocle.EntityList.DebugRender += EntityListOnDebugRender;
        }

        public static void Unload() {
            On.Monocle.EntityList.DebugRender -= EntityListOnDebugRender;
        }

        private static void EntityListOnDebugRender(On.Monocle.EntityList.orig_DebugRender orig, EntityList self, Camera camera) {
            orig(self, camera);
            if (CelesteTasModule.Settings.ShowHitboxes && CelesteTasModule.Settings.CenterCamera && self.Scene is Level level &&
                level.GetPlayer() is { } player) {
                Rectangle bounds = level.Bounds;
                float topExtra = player.CenterY - player.Top + 1;
                Draw.HollowRect(bounds.X - 1, bounds.Y - topExtra, bounds.Width + 2, bounds.Height + topExtra + 1, HitboxColor.TriggerColor);
            }
        }
    }
}