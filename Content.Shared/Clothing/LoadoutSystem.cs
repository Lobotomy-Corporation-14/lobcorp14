using System.Linq;
using Content.Shared.Body.Systems;
using Content.Shared.Clothing.Components;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Content.Shared.Station;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared.Clothing;

/// <summary>
/// Assigns a loadout to an entity based on the RoleLoadout prototype
/// </summary>
public sealed class LoadoutSystem : EntitySystem
{
    // Shared so we can predict it for placement manager.

    [Dependency] private readonly ActorSystem _actors = default!;
    [Dependency] private readonly SharedStationSpawningSystem _station = default!;
    [Dependency] private readonly IPrototypeManager _protoMan = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Wait until the character has all their organs before we give them their loadout
        SubscribeLocalEvent<LoadoutComponent, MapInitEvent>(OnMapInit, after: [typeof(SharedBodySystem)]);
    }

    private void OnMapInit(EntityUid uid, LoadoutComponent component, MapInitEvent args)
    {
        // Use starting gear if specified
        if (component.StartingGear != null)
        {
            var gear = _protoMan.Index(_random.Pick(component.StartingGear));
            _station.EquipStartingGear(uid, gear);
            return;
        }

        if (component.RoleLoadout == null)
            return;

        // ...otherwise equip from role loadout
        var id = _random.Pick(component.RoleLoadout);
        var proto = _protoMan.Index(id);
        var loadout = new RoleLoadout(id);
        loadout.SetDefault(GetProfile(uid), _actors.GetSession(uid), _protoMan, true);
        _station.EquipRoleLoadout(uid, loadout, proto);
    }

    public HumanoidCharacterProfile GetProfile(EntityUid? uid)
    {
        if (TryComp(uid, out HumanoidAppearanceComponent? appearance))
        {
            return HumanoidCharacterProfile.DefaultWithSpecies(appearance.Species);
        }

        return HumanoidCharacterProfile.Random();
    }
}
