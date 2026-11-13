using System.Security.Cryptography;
using Content.Client._Starlight.Overlay;
using Content.Client._Starlight.Shadekin;
using Content.Shared._KB.Shadekin;
using Content.Shared._Starlight.Shadekin;
using Content.Shared._Starlight.Vision;
using Content.Shared.EntityEffects.Effects;
using Content.Shared.Examine;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Flash.Components;
using Content.Shared.Humanoid;
using Content.Shared.Inventory.Events;
using Content.Shared.Light.Components;
using Content.Shared.Mech;
using Content.Shared.Mech.Components;
using Content.Shared.Starlight.Overlay;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Physics;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._KB.Shadekin;

public sealed class ShadekinVisionSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IOverlayManager _overlayMan = default!;
    [Dependency] private readonly TransformSystem _xformSys = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly FlashImmunitySystem _flashImmunity = default!;
    [Dependency] private readonly SharedPointLightSystem _pointLight = default!;
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ContainerSystem _container = default!;
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly IGameTiming _timing = default!;


    private ISawmill _sawmill = default!;


    private NightVisionOverlay _overlay = default!;
    [ViewVariables]
    int traced = 7;

    private EntityUid _kin = default!; 
    private EntityUid _effect = default!;
    private bool _applied = false;
    private bool _zeroed = false;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShadekinVisionComponent, ComponentInit>(OnVisionInit);
        SubscribeLocalEvent<ShadekinVisionComponent, ComponentShutdown>(OnVisionShutdown);

        SubscribeLocalEvent<ShadekinVisionComponent, LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<ShadekinVisionComponent, LocalPlayerDetachedEvent>(OnPlayerDetached);

        SubscribeLocalEvent<ShadekinVisionComponent, FlashImmunityCheckEvent>(OnFlashImmunityChanged);
        SubscribeLocalEvent<ShadekinVisionComponent, AfterAutoHandleStateEvent>(OnAutoHandleEvent);
    }
    public override void FrameUpdate(float frameTime)
    {
        _sawmill = _logManager.GetSawmill("Shadekin");
        base.FrameUpdate(frameTime);

        if (!_applied)
            return;

        if (_timing.FramesPerSecondAvg > 60 && _timing.CurFrame % 3 != 0)
            return;

        if (_kin == default)
            // TODO: Something should go here for fringe weirdnesses, but if that happened something's going VERY wrong elsewhere I and just wanna get it done for now.
            return;

        if (_effect == default)
            // TODO: Same here. Not gonna have it just re-add the effect for risk of doubling up.
            return;

        float glow = 0.8f;

        float light = GetLightExposure(_kin) / 2f;

        _sawmill.Error($"{light}");

        if (light == 0)
        {
            if (_zeroed)
                return;
            else if (TryComp<PointLightComponent>(_effect, out var zerocomp))
            { 
                _pointLight.SetEnergy(_kin, glow, zerocomp);
                Dirty(_kin, zerocomp);
                _zeroed = true;
            }
            return;
        }

        _zeroed = false;

        glow -= light > 0.8 ? glow : light;

        if (TryComp<PointLightComponent>(_effect, out var comp))
        {
            _pointLight.SetEnergy(_kin, glow, comp);
            Dirty(_kin, comp);
        }

    }
    private void OnFlashImmunityChanged(Entity<ShadekinVisionComponent> ent, ref FlashImmunityCheckEvent args)
    {
        if (args.IsImmune)
        {
            AttemptRemoveVision(ent.Owner);
        }
        else
        {
            AttemptAddVision(ent.Owner);
        }
    }

    private void OnAutoHandleEvent(Entity<ShadekinVisionComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _sawmill = _logManager.GetSawmill("Shadekin");
        if (ent.Comp.Active)
        {
            AttemptAddVision(ent.Owner);
        }
        else
        {
            AttemptRemoveVision(ent.Owner);
        }
    }
    private void OnPlayerAttached(Entity<ShadekinVisionComponent> ent, ref LocalPlayerAttachedEvent args)
    {
        AttemptAddVision(ent.Owner);
    }

    private void OnPlayerDetached(Entity<ShadekinVisionComponent> ent, ref LocalPlayerDetachedEvent args)
    {
        AttemptRemoveVision(ent.Owner, true);
    }
    private void OnVisionInit(Entity<ShadekinVisionComponent> ent, ref ComponentInit args)
    {
        AttemptAddVision(ent.Owner);
    }

    private void OnVisionShutdown(Entity<ShadekinVisionComponent> ent, ref ComponentShutdown args)
    {
        AttemptRemoveVision(ent.Owner);
    }
    private void AttemptAddVision(EntityUid uid)
    {
        if (_player.LocalSession?.AttachedEntity != uid)
            return;

        _kin = uid;

        if (!TryComp<ShadekinVisionComponent>(uid, out var visioncomp))
            return;

        if (_flashImmunity.HasFlashImmunityVisionBlockers(uid))
            return;

        if (_effect == default)
        {
            if (!TryComp<HumanoidAppearanceComponent>(uid, out var humanoid))
                return;
            var glow = Color.ToHsv(humanoid.EyeColor);

            glow.Y = 0.25f;
            glow.Z = 1;

            _effect = SpawnAttachedTo("EffectNightVision", Transform(uid).Coordinates);
            _xformSys.SetParent(_effect, uid);
            if (TryComp<PointLightComponent>(_effect, out var light))
            _pointLight.SetColor(_effect, Color.FromHsv(glow), light);
            _applied = true;
        }
    }
    private double GetAngleDiff(EntityUid lightUid, SharedPointLightComponent lightComp, EntityUid targetUid)
    {
        var (lightPos, lightRot) = _transform.GetWorldPositionRotation(lightUid);

        var targetPos = _transform.GetWorldPosition(targetUid);

        var mapDiff = targetPos - lightPos;

        var angleRaw = Math.Atan2(mapDiff.Y, mapDiff.X);

        // This is cursed because I guess stuff points 90 degrees off the actual cone, because of course. I'm probably getting it wrong or something
        var angle = (((lightRot - angleRaw) * (180.0 / Math.PI)) - 90.0 + 360.0) % 360.0;

        var angleDiff = angle > 180f ? 360f - angle : angle;

        return angleDiff;
    }
    public float GetLightExposure(EntityUid uid)
    {
        var illumination = 0f;

        var shadeQuery = _lookup.GetEntitiesInRange<ShadegenComponent>(Transform(uid).Coordinates, 10); // Why 10 when theres different ranges? because light check does not go above 20.

        foreach (var shadegen in shadeQuery)
            if (_transform.InRange(Transform(uid).Coordinates, Transform(shadegen.Owner).Coordinates, shadegen.Comp.Range))
                return illumination;

        var lightQuery = _lookup.GetEntitiesInRange<PointLightComponent>(Transform(uid).Coordinates, 10, LookupFlags.All | LookupFlags.Approximate);

        foreach (var light in lightQuery)
        {
            if (HasComp<DarkLightComponent>(light.Owner))
                continue;
            
            // Emergency lights are a nightmare.
            if (HasComp<RotatingLightComponent>(light.Owner))
                continue;

            if (!light.Comp.Enabled || light.Comp.Energy <= 0)
                continue;

            var multipler = 1f;


            if (light.Comp.Radius <= 1.5)
            {
                if (light.Comp.Radius <= 1)
                    multipler = 0.66f;
                else
                    multipler = 0.33f;
            }

            // Check if our entity is in a container with OccludesLight, if yes, is it the same as the light?
            if (_container.TryGetContainingContainer(uid, out var uidcontainer) && uidcontainer.OccludesLight && !_container.IsInSameOrNoContainer(uid, light.Owner))
                continue;

            // Same as above but this time we check the light entity instead of our entity.
            if (_container.TryGetContainingContainer(light.Owner, out var lightcontainer) && lightcontainer.OccludesLight && !_container.IsInSameOrNoContainer(uid, light.Owner))
                continue;

            if (!_examine.InRangeUnOccluded(light, uid, light.Comp.Radius, null))
                continue;

            Transform(uid).Coordinates.TryDistance(EntityManager, Transform(light).Coordinates, out var dist);

            var denom = dist / light.Comp.Radius;
            var attenuation = 1 - (denom * denom);
            var calculatedLight = 0f;

            if (light.Comp.LightMask is { } maskId && _prototypeManager.TryIndex(maskId, out var mask))
            {
                var diff = GetAngleDiff(light, light.Comp, uid);
                foreach (var cone in mask.LightCones)
                {
                    if (diff > cone.OuterWidth.Degrees / 2)
                        continue;
                    var coneLight = light.Comp.Energy;
                    if (diff > cone.InnerWidth.Degrees / 2)
                    {
                        double close = (cone.InnerWidth.Degrees / 2) - diff;
                        double range = (cone.InnerWidth.Degrees - cone.OuterWidth.Degrees) / 2;
                        coneLight *= 1 - (float)(close / range);
                    }
                    calculatedLight = coneLight * attenuation * multipler;
                }
            }
            else
                calculatedLight = light.Comp.Energy * attenuation * multipler;

            illumination += calculatedLight;
        }
        return illumination;
    }
    private void AttemptRemoveVision(EntityUid uid, bool force = false)
    {
        //ENSURE this is the local player
        if (_player.LocalSession?.AttachedEntity != uid && !force) return;
        QueueDel(_effect);
        _effect = default;
        _kin = default;
    }
}
