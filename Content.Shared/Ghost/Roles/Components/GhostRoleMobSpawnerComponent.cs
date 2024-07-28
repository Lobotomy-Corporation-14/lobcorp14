using Robust.Shared.Prototypes;

namespace Content.Shared.Ghost.Roles.Components
{
    /// <summary>
    ///     Allows a ghost to take this role, spawning a new entity.
    /// </summary>
    [RegisterComponent, EntityCategory("Spawner")]
<<<<<<< HEAD:Content.Server/Ghost/Roles/Components/GhostRoleMobSpawnerComponent.cs
    [Access(typeof(GhostRoleSystem))]
=======
>>>>>>> fce5269fc0b243b78a8742924f97f31807462877:Content.Shared/Ghost/Roles/Components/GhostRoleMobSpawnerComponent.cs
    public sealed partial class GhostRoleMobSpawnerComponent : Component
    {
        [DataField]
        public bool DeleteOnSpawn = true;

        [DataField]
        public int AvailableTakeovers = 1;

        [ViewVariables]
        public int CurrentTakeovers = 0;

        [DataField]
        public EntProtoId? Prototype;

        /// <summary>
        ///     If this ghostrole spawner has multiple selectable ghostrole prototypes.
        /// </summary>
        [DataField]
        public List<string> SelectablePrototypes = [];
    }
}
