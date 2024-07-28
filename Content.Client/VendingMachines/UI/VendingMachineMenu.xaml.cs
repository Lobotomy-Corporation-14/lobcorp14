using System.Numerics;
using Content.Shared.VendingMachines;
using Robust.Client.AutoGenerated;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Prototypes;
using FancyWindow = Content.Client.UserInterface.Controls.FancyWindow;
using Content.Shared.IdentityManagement;
using Robust.Shared.Timing;

namespace Content.Client.VendingMachines.UI
{
    [GenerateTypedNameReferences]
    public sealed partial class VendingMachineMenu : FancyWindow
    {
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly IEntityManager _entityManager = default!;
<<<<<<< HEAD
        [Dependency] private readonly IGameTiming _timing = default!;
=======
>>>>>>> fce5269fc0b243b78a8742924f97f31807462877

        private readonly Dictionary<EntProtoId, EntityUid> _dummies = [];

        public event Action<ItemList.ItemListSelectedEventArgs>? OnItemSelected;
        public event Action<string>? OnSearchChanged;

        public VendingMachineMenu()
        {
            MinSize = SetSize = new Vector2(250, 150);
            RobustXamlLoader.Load(this);
            IoCManager.InjectDependencies(this);

            SearchBar.OnTextChanged += _ =>
            {
                OnSearchChanged?.Invoke(SearchBar.Text);
            };

            VendingContents.OnItemSelected += args =>
            {
                OnItemSelected?.Invoke(args);
            };
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            // Don't clean up dummies during disposal or we'll just have to spawn them again
            if (!disposing)
                return;

            // Delete any dummy items we spawned
            foreach (var entity in _dummies.Values)
            {
                _entityManager.QueueDeleteEntity(entity);
            }
            _dummies.Clear();
        }

        /// <summary>
        /// Populates the list of available items on the vending machine interface
        /// and sets icons based on their prototypes
        /// </summary>
        public void Populate(List<VendingMachineInventoryEntry> inventory, out List<int> filteredInventory,  string? filter = null)
        {
            filteredInventory = new();

            if (inventory.Count == 0)
            {
                VendingContents.Clear();
                var outOfStockText = Loc.GetString("vending-machine-component-try-eject-out-of-stock");
                VendingContents.AddItem(outOfStockText);
                SetSizeAfterUpdate(outOfStockText.Length, VendingContents.Count);
                return;
            }

            while (inventory.Count != VendingContents.Count)
            {
                if (inventory.Count > VendingContents.Count)
                    VendingContents.AddItem(string.Empty);
                else
                    VendingContents.RemoveAt(VendingContents.Count - 1);
            }

            var longestEntry = string.Empty;
            var spriteSystem = IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<SpriteSystem>();

            var filterCount = 0;
            for (var i = 0; i < inventory.Count; i++)
            {
                var entry = inventory[i];
                var vendingItem = VendingContents[i - filterCount];
                vendingItem.Text = string.Empty;
                vendingItem.Icon = null;

                if (!_dummies.TryGetValue(entry.ID, out var dummy))
                {
                    dummy = _entityManager.Spawn(entry.ID);
                    _dummies.Add(entry.ID, dummy);
                }

                var itemName = Identity.Name(dummy, _entityManager);
                Texture? icon = null;
                if (_prototypeManager.TryIndex<EntityPrototype>(entry.ID, out var prototype))
                {
                    icon = spriteSystem.GetPrototypeIcon(prototype).Default;
                }

                // search filter
                if (!string.IsNullOrEmpty(filter) &&
                    !itemName.ToLowerInvariant().Contains(filter.Trim().ToLowerInvariant()))
                {
                    VendingContents.Remove(vendingItem);
                    filterCount++;
                    continue;
                }

                if (itemName.Length > longestEntry.Length)
                    longestEntry = itemName;

                vendingItem.Text = $"{itemName} [{entry.Amount}]";
                vendingItem.Icon = icon;
                filteredInventory.Add(i);
            }

            SetSizeAfterUpdate(longestEntry.Length, inventory.Count);
        }

        private void SetSizeAfterUpdate(int longestEntryLength, int contentCount)
        {
            SetSize = new Vector2(Math.Clamp((longestEntryLength + 2) * 12, 250, 300),
                Math.Clamp(contentCount * 50, 150, 350));
        }
    }
}
