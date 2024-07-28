using System.Linq;
using Content.Shared.Salvage;
using Content.Shared.Salvage.Magnet;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Salvage.UI;

public sealed class SalvageMagnetBoundUserInterface : BoundUserInterface
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    private OfferingWindow? _window;

    public SalvageMagnetBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Open()
    {
        base.Open();

<<<<<<< HEAD
        if (_window is null)
        {
            _window = new OfferingWindow();
            _window.Title = Loc.GetString("salvage-magnet-window-title");
            _window.OnClose += Close;
            _window.OpenCenteredLeft();
        }
=======
        _window = this.CreateWindow<OfferingWindow>();
        _window.Title = Loc.GetString("salvage-magnet-window-title");
        _window.OpenCenteredLeft();
>>>>>>> fce5269fc0b243b78a8742924f97f31807462877
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not SalvageMagnetBoundUserInterfaceState current || _window == null)
            return;

        _window.ClearOptions();

        var salvageSystem = _entManager.System<SharedSalvageSystem>();
        _window.NextOffer = current.NextOffer;
        _window.Progression = current.EndTime ?? TimeSpan.Zero;
        _window.Claimed = current.EndTime != null;
        _window.Cooldown = current.Cooldown;
        _window.ProgressionCooldown = current.Duration;

        for (var i = 0; i < current.Offers.Count; i++)
        {
            var seed = current.Offers[i];
            var offer = salvageSystem.GetSalvageOffering(seed);
            var option = new OfferingWindowOption();
            option.MinWidth = 210f;
            option.Disabled = current.EndTime != null;
            option.Claimed = current.ActiveSeed == seed;
            var claimIndex = i;

            option.ClaimPressed += args =>
            {
                SendMessage(new MagnetClaimOfferEvent()
                {
                    Index = claimIndex
                });
            };

            switch (offer)
            {
                case AsteroidOffering asteroid:
                    option.Title = Loc.GetString($"dungeon-config-proto-{asteroid.DungeonConfig.ID}");
                    var layerKeys = asteroid.MarkerLayers.Keys.ToList();
                    layerKeys.Sort();

                    foreach (var resource in layerKeys)
                    {
                        var count = asteroid.MarkerLayers[resource];

                        var container = new BoxContainer()
                        {
                            Orientation = BoxContainer.LayoutOrientation.Horizontal,
                            HorizontalExpand = true,
                        };

                        var resourceLabel = new Label()
                        {
                            Text = Loc.GetString("salvage-magnet-resources",
                                ("resource", resource)),
                            HorizontalAlignment = Control.HAlignment.Left,
                        };

                        var countLabel = new Label()
                        {
                            Text = Loc.GetString("salvage-magnet-resources-count", ("count", count)),
                            HorizontalAlignment = Control.HAlignment.Right,
                            HorizontalExpand = true,
                        };

                        container.AddChild(resourceLabel);
                        container.AddChild(countLabel);

                        option.AddContent(container);
                    }

                    break;
                case SalvageOffering salvage:
                    option.Title = Loc.GetString($"salvage-map-proto-{salvage.SalvageMap.ID}");
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            _window.AddOption(option);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _window?.Close();
            _window?.Dispose();
        }
    }
}
