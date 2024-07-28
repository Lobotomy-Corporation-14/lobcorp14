using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using JetBrains.Annotations;
<<<<<<< HEAD
=======
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
>>>>>>> fce5269fc0b243b78a8742924f97f31807462877

namespace Content.Client.Radio.Ui;

[UsedImplicitly]
public sealed class IntercomBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private IntercomMenu? _menu;

    public IntercomBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {

    }

    protected override void Open()
    {
        base.Open();

<<<<<<< HEAD
        var comp = EntMan.GetComponent<IntercomComponent>(Owner);

        _menu = new((Owner, comp));
=======
        _menu = this.CreateWindow<IntercomMenu>();

        if (EntMan.TryGetComponent(Owner, out IntercomComponent? intercom))
        {
            _menu.Update((Owner, intercom));
        }
>>>>>>> fce5269fc0b243b78a8742924f97f31807462877

        _menu.OnMicPressed += enabled =>
        {
            SendMessage(new ToggleIntercomMicMessage(enabled));
        };
        _menu.OnSpeakerPressed += enabled =>
        {
            SendMessage(new ToggleIntercomSpeakerMessage(enabled));
        };
        _menu.OnChannelSelected += channel =>
        {
            SendMessage(new SelectIntercomChannelMessage(channel));
        };
    }

    public void Update(Entity<IntercomComponent> ent)
    {
<<<<<<< HEAD
        base.Dispose(disposing);
        if (!disposing)
            return;
        _menu?.Close();
    }

    public void Update(Entity<IntercomComponent> ent)
    {
=======
>>>>>>> fce5269fc0b243b78a8742924f97f31807462877
        _menu?.Update(ent);
    }
}
