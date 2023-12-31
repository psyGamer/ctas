﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.Module;
using TAS.Utils;

using Platform = Celeste.Platform;

namespace TAS.EverestInterop.InfoHUD;

public enum WatchEntityType {
    Position,
    DeclaredOnly,
    Auto,
    All
}

public static partial class InfoWatchEntity {
    // ReSharper disable UnusedMember.Local
    private record struct MemberKey(Type Type, bool DeclaredOnly) {
        public readonly Type Type = Type;
        public readonly bool DeclaredOnly = DeclaredOnly;
    }
    // ReSharper restore UnusedMember.Local

    private static readonly Dictionary<MemberKey, List<MemberInfo>> CachedMemberInfos = new();
    private static readonly WeakReference<Entity> LastClickedEntity = new(null);
    private static AreaKey requireWatchAreaKey;

    public static void CheckMouseButtons() {
        if (MouseButtons.Right.Pressed) {
            ClearWatchEntities(clearCheckList: true);
        }

        if (MouseButtons.Left.Pressed && !IsClickHud() && FindClickedEntity() is { } entity) {
            ToggleWatching(entity);
            PrintAllSimpleValues(entity);
        }
    }

    private static bool IsClickHud() {
        Rectangle rectangle = new((int) TasSettings.InfoPosition.X, (int) TasSettings.InfoPosition.Y, (int) InfoHud.Size.X, (int) InfoHud.Size.Y);
        return rectangle.Contains((int) MouseButtons.Position.X, (int) MouseButtons.Position.Y);
    }

    private static List<Entity> FindClickedEntities() {
        if (Engine.Scene is Level level) {
            Vector2 mouseWorldPosition = level.MouseToWorld(MouseButtons.Position);
            Entity tempEntity = new() {Position = mouseWorldPosition, Collider = new Hitbox(1, 1)};
            List<Entity> allEntities = level.Entities.Where(entity =>
                entity.GetType() != typeof(Entity)
                && entity is not ParticleSystem).ToList();

            List<Entity> noColliderEntities = allEntities.Where(entity =>
                entity.Collider == null
                && entity.GetEntityData() != null
            ).ToList();

            foreach (Entity entity in noColliderEntities) {
                EntityData data = entity.GetEntityData();
                entity.Collider = new Hitbox(data.Width, data.Height);
            }

            List<Entity> result = allEntities.Where(entity => entity.CollideCheck(tempEntity)).ToList();

            foreach (Entity entity in noColliderEntities) {
                entity.Collider = null;
            }

            // put trigger after entity
            result.Sort((entity1, entity2) => (entity1 is Trigger ? 1 : -1) - (entity2 is Trigger ? 1 : -1));
            return result;
        } else {
            return new List<Entity>();
        }
    }

    public static Entity FindClickedEntity() {
        List<Entity> clickedEntities = FindClickedEntities();

        Entity clickedEntity;
        if (LastClickedEntity.TryGetTarget(out Entity lastClicked) && clickedEntities.IndexOf(lastClicked) is int index and >= 0) {
            clickedEntity = clickedEntities[(index + 1) % clickedEntities.Count];
        } else {
            clickedEntity = clickedEntities.FirstOrDefault();
        }

        LastClickedEntity.SetTarget(clickedEntity);
        return clickedEntity;
    }

    public static string GetInfo(string separator = "\n", bool alwaysUpdate = false, int? decimals = null) {
        string watchingInfo = string.Empty;
        if (Engine.Scene is not Level level || TasSettings.InfoWatchEntity == HudOptions.Off && !alwaysUpdate) {
            return string.Empty;
        }

        decimals ??= TasSettings.CustomInfoDecimals;

        return string.Join(separator, WatchingList.Tuples.Where((tuple) => tuple.Item2.Target is Entity { Scene: { } }).Select((tuple) => {
            return GetEntityValues((Entity) tuple.Item2.Target, TasSettings.InfoWatchEntityType, separator, decimals.Value);
        }));
    }

    private static void PrintAllSimpleValues(Entity entity) {
        ("Info of Clicked Entity:\n" + GetEntityValues(entity, WatchEntityType.All)).Log(true);
    }

    private static string GetEntityValues(Entity entity, WatchEntityType watchEntityType, string separator = "\n", int decimals = 2) {
        Type type = entity.GetType();
        string entityId = "";
        if (entity.GetEntityData() is { } entityData) {
            entityId = $"[{entityData.ToEntityId().ToString()}]";
        }

        if (watchEntityType == WatchEntityType.Position) {
            return GetPositionInfo(entity, entityId, decimals);
        }
        else if (watchEntityType == WatchEntityType.Auto) {
            return GetAutoWatchInfo(entity, separator, decimals, entityId);
        }

        List<string> values = GetAllSimpleFields(type, watchEntityType == WatchEntityType.DeclaredOnly).Select(info => {
            object value;
            try {
                value = info switch {
                    FieldInfo fieldInfo => fieldInfo.GetValue(entity),
                    PropertyInfo propertyInfo => propertyInfo.GetValue(entity),
                    _ => null
                };
            } catch {
                value = string.Empty;
            }

            if (value is float floatValue) {
                if (info.Name.EndsWith("Timer")) {
                    value = GameInfo.ConvertToFrames(floatValue);
                } else {
                    value = floatValue.ToFormattedString(decimals);
                }
            } else if (value is Vector2 vector2) {
                value = vector2.ToSimpleString(decimals);
            }

            if (separator == "\t" && value != null) {
                value = value.ToString().ReplaceLineBreak(" ");
            }

            return $"{type.Name}{entityId}.{info.Name}: {value}";
        }).ToList();

        values.Insert(0, GetPositionInfo(entity, entityId, decimals));

        return string.Join(separator, values);
    }

    private static string GetPositionInfo(Entity entity, string entityId, int decimals) {
        return $"{entity.GetType().Name}{entityId}: {entity.ToSimplePositionString(decimals)}";
    }

    private static IEnumerable<MemberInfo> GetAllSimpleFields(Type type, bool declaredOnly = false) {
        MemberKey key = new(type, declaredOnly);

        if (CachedMemberInfos.TryGetValue(key, out List<MemberInfo> result)) {
            return result;
        } else {
            CachedMemberInfos[key] = result = new List<MemberInfo>();

            FieldInfo[] fields;
            PropertyInfo[] properties;

            if (declaredOnly) {
                BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                fields = type.GetFields(bindingFlags);
                properties = type.GetProperties(bindingFlags);
            } else {
                fields = type.GetAllFieldInfos().ToArray();
                properties = type.GetAllProperties().ToArray();
            }

            List<MemberInfo> memberInfos = fields.Where(info => info.FieldType.IsSimpleType() && !info.Name.EndsWith("k__BackingField"))
                .Cast<MemberInfo>().ToList();
            List<MemberInfo> propertyInfos = properties.Where(info => info.PropertyType.IsSimpleType()).Cast<MemberInfo>().ToList();
            memberInfos.AddRange(propertyInfos);

            foreach (IGrouping<bool, MemberInfo> grouping in memberInfos.GroupBy(info => type == info.DeclaringType)) {
                List<MemberInfo> infos = grouping.ToList();
                infos.Sort((info1, info2) => string.Compare(info1.Name, info2.Name, StringComparison.InvariantCultureIgnoreCase));
                if (grouping.Key) {
                    result.InsertRange(0, infos);
                } else {
                    result.AddRange(infos);
                }
            }

            return result;
        }
    }

    public static Vector2 ClampLiftSpeed(this Vector2 liftspeed) {
        return liftspeed.Clamp(-250f, -130f, 250f, 0f);
    }
}

public static class EntityStates {
    private static readonly IDictionary<int, string> SeekerStates = new Dictionary<int, string> {
        {Seeker.StIdle, nameof(Seeker.StIdle)},
        {Seeker.StPatrol, nameof(Seeker.StPatrol)},
        {Seeker.StSpotted, nameof(Seeker.StSpotted)},
        {Seeker.StAttack, nameof(Seeker.StAttack)},
        {Seeker.StStunned, nameof(Seeker.StStunned)},
        {Seeker.StSkidding, nameof(Seeker.StSkidding)},
        {Seeker.StRegenerate, nameof(Seeker.StRegenerate)},
        {Seeker.StReturned, nameof(Seeker.StReturned)},
    };
    private static readonly IDictionary<int, string> OshiroStates = new Dictionary<int, string> {
        {AngryOshiro.StChase, nameof(AngryOshiro.StChase)},
        {AngryOshiro.StChargeUp, nameof(AngryOshiro.StChargeUp)},
        {AngryOshiro.StAttack, nameof(AngryOshiro.StAttack)},
        {AngryOshiro.StDummy, nameof(AngryOshiro.StDummy)},
        {AngryOshiro.StWaiting, nameof(AngryOshiro.StWaiting)},
        {AngryOshiro.StHurt, nameof(AngryOshiro.StHurt)},
    };

    public static string GetStateName(this AngryOshiro oshiro) {
        int state = oshiro.state.State;
        return SeekerStates.TryGetValue(state, out string name) ? name : state.ToString();
    }

    public static string GetStateName(this Seeker seeker) {
        int state = seeker.State.State;
        return SeekerStates.TryGetValue(state, out string name) ? name : state.ToString();
    }
}