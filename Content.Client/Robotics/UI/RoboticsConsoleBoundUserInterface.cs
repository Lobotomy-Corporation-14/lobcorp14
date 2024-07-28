using Content.Shared.Robotics;
using Robust.Client.GameObjects;
<<<<<<< HEAD
=======
using Robust.Client.UserInterface;
>>>>>>> fce5269fc0b243b78a8742924f97f31807462877

namespace Content.Client.Robotics.UI;

public sealed class RoboticsConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    public RoboticsConsoleWindow _window = default!;

    public RoboticsConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

<<<<<<< HEAD
        _window = new RoboticsConsoleWindow(Owner);
=======
        _window = this.CreateWindow<RoboticsConsoleWindow>();
        _window.SetEntity(Owner);

>>>>>>> fce5269fc0b243b78a8742924f97f31807462877
        _window.OnDisablePressed += address =>
        {
            SendMessage(new RoboticsConsoleDisableMessage(address));
        };
        _window.OnDestroyPressed += address =>
        {
            SendMessage(new RoboticsConsoleDestroyMessage(address));
        };
<<<<<<< HEAD
        _window.OnClose += Close;

        _window.OpenCentered();
=======
>>>>>>> fce5269fc0b243b78a8742924f97f31807462877
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not RoboticsConsoleState cast)
            return;

<<<<<<< HEAD
        _window?.UpdateState(cast);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            _window?.Dispose();
=======
        _window.UpdateState(cast);
>>>>>>> fce5269fc0b243b78a8742924f97f31807462877
    }
}
