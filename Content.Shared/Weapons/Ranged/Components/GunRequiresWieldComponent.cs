using Content.Shared.Wieldable;
using Robust.Shared.GameStates;

namespace Content.Shared.Weapons.Ranged.Components;

/// <summary>
/// Indicates that this gun requires wielding to be useable.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(WieldableSystem))]
public sealed partial class GunRequiresWieldComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan LastPopup;

    [DataField, AutoNetworkedField]
    public TimeSpan PopupCooldown = TimeSpan.FromSeconds(1);
<<<<<<< HEAD
=======

    [DataField]
    public LocId? WieldRequiresExamineMessage  = "gunrequireswield-component-examine";
>>>>>>> fce5269fc0b243b78a8742924f97f31807462877
}
