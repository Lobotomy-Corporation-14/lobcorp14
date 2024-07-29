using Content.Shared.UserInterface;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Popups;
using Robust.Shared.Collections;

namespace Content.Shared.Eye.Blinding.Systems;

public sealed class ActivatableUIRequiresVisionSystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _userInterfaceSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ActivatableUIRequiresVisionComponent, ActivatableUIOpenAttemptEvent>(OnOpenAttempt);
        SubscribeLocalEvent<BlindableComponent, BlindnessChangedEvent>(OnBlindnessChanged);
    }

    private void OnOpenAttempt(EntityUid uid, ActivatableUIRequiresVisionComponent component, ActivatableUIOpenAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (TryComp<BlindableComponent>(args.User, out var blindable) && blindable.IsBlind)
        {
            _popupSystem.PopupClient(Loc.GetString("blindness-fail-attempt"), args.User, Shared.Popups.PopupType.MediumCaution);
            args.Cancel();
        }
    }

    private void OnBlindnessChanged(EntityUid uid, BlindableComponent component, ref BlindnessChangedEvent args)
    {
        if (!args.Blind)
            return;

        if (!TryComp<ActorComponent>(uid, out var actor))
            return;

        var uiList = _userInterfaceSystem.GetAllUIsForSession(actor.PlayerSession);
        if (uiList == null)
            return;

        Queue<PlayerBoundUserInterface> closeList = new(); // foreach collection modified moment

        foreach (var ui in uiList)
        {
            if (HasComp<ActivatableUIRequiresVisionComponent>(ui.Owner))
                closeList.Enqueue(ui);
        }

        foreach (var ui in closeList)
        {
            _userInterfaceSystem.CloseUi(ui, actor.PlayerSession);
        }
    }
}
