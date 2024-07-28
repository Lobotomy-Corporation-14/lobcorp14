using Content.Shared.Atmos.Components;
using JetBrains.Annotations;
<<<<<<< HEAD
=======
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
>>>>>>> fce5269fc0b243b78a8742924f97f31807462877

namespace Content.Client.UserInterface.Systems.Atmos.GasTank
{
    [UsedImplicitly]
    public sealed class GasTankBoundUserInterface : BoundUserInterface
    {
        [ViewVariables]
        private GasTankWindow? _window;

        public GasTankBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
        }

        public void SetOutputPressure(float value)
        {
            SendMessage(new GasTankSetPressureMessage
            {
                Pressure = value
            });
        }

        public void ToggleInternals()
        {
            SendMessage(new GasTankToggleInternalsMessage());
        }

        protected override void Open()
        {
            base.Open();
<<<<<<< HEAD
            _window = new GasTankWindow(this, EntMan.GetComponent<MetaDataComponent>(Owner).EntityName);
            _window.OnClose += Close;
            _window.OpenCentered();
=======
            _window = this.CreateWindow<GasTankWindow>();
            _window.SetTitle(EntMan.GetComponent<MetaDataComponent>(Owner).EntityName);
            _window.OnOutputPressure += SetOutputPressure;
            _window.OnToggleInternals += ToggleInternals;
>>>>>>> fce5269fc0b243b78a8742924f97f31807462877
        }

        protected override void UpdateState(BoundUserInterfaceState state)
        {
            base.UpdateState(state);

            if (state is GasTankBoundUserInterfaceState cast)
                _window?.UpdateState(cast);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            _window?.Close();
        }
    }
}
