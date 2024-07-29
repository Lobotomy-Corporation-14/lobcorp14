using Robust.Shared.Timing;

namespace Content.Shared.Timing;

public sealed class UseDelaySystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly MetaDataSystem _metadata = default!;

    public void SetDelay(Entity<UseDelayComponent> ent, TimeSpan delay)
    {
        if (ent.Comp.Delay == delay)
            return;

        ent.Comp.Delays.Clear();

        // At time of writing sourcegen networking doesn't deep copy so this will mispredict if you try.
        foreach (var (key, delay) in state.Delays)
        {
            ent.Comp.Delays[key] = new UseDelayInfo(delay.Length, delay.StartTime, delay.EndTime);
        }
    }

    private void OnDelayGetState(Entity<UseDelayComponent> ent, ref ComponentGetState args)
    {
        args.State = new UseDelayComponentState()
        {
            Delays = ent.Comp.Delays
        };
    }

    private void OnMapInit(Entity<UseDelayComponent> ent, ref MapInitEvent args)
    {
        // Set default delay length from the prototype
        // This makes it easier for simple use cases that only need a single delay
        SetLength((ent, ent.Comp), ent.Comp.Delay, DefaultId);
    }

    private void OnUnpaused(Entity<UseDelayComponent> ent, ref EntityUnpausedEvent args)
    {
        // We have to do this manually, since it's not just a single field.
        foreach (var entry in ent.Comp.Delays.Values)
        {
            entry.EndTime += args.PausedTime;
        }
    }

    /// <summary>
    /// Sets the length of the delay with the specified ID.
    /// </summary>
    /// <remarks>
    /// This will add a UseDelay component to the entity if it doesn't have one.
    /// </remarks>
    public bool SetLength(Entity<UseDelayComponent?> ent, TimeSpan length, string id = DefaultId)
    {
        EnsureComp<UseDelayComponent>(ent.Owner, out var comp);

        if (comp.Delays.TryGetValue(id, out var entry))
        {
            if (entry.Length == length)
                return true;

            entry.Length = length;
        }
        else
        {
            comp.Delays.Add(id, new UseDelayInfo(length));
        }

        Dirty(ent);
        return true;
    }

    /// <summary>
    /// Returns true if the entity has a currently active UseDelay with the specified ID.
    /// </summary>
    public bool IsDelayed(Entity<UseDelayComponent> ent, string id = DefaultId)
    {
        if (!ent.Comp.Delays.TryGetValue(id, out var entry))
            return false;

        return entry.EndTime >= _gameTiming.CurTime;
    }

    /// <summary>
    /// Cancels the delay with the specified ID.
    /// </summary>
    public void CancelDelay(Entity<UseDelayComponent> ent, string id = DefaultId)
    {
        if (!ent.Comp.Delays.TryGetValue(id, out var entry))
            return;

        entry.EndTime = _gameTiming.CurTime;
        Dirty(ent);
    }

    /// <summary>
    /// Returns true if the entity has a currently active UseDelay.
    /// </summary>
    public bool IsDelayed(Entity<UseDelayComponent> ent)
    {
        return ent.Comp.DelayEndTime >= _gameTiming.CurTime;
    }

    /// <summary>
    /// Cancels the current delay.
    /// </summary>
    public void CancelDelay(Entity<UseDelayComponent> ent)
    {
        ent.Comp.DelayEndTime = _gameTiming.CurTime;
        Dirty(ent);
    }

    /// <summary>
    /// Resets the UseDelay entirely for this entity if possible.
    /// </summary>
    /// <param name="checkDelayed">Check if the entity has an ongoing delay, return false if it does, return true if it does not.</param>
    public bool TryResetDelay(Entity<UseDelayComponent> ent, bool checkDelayed = false)
    {
        if (checkDelayed && IsDelayed(ent))
            return false;

        var curTime = _gameTiming.CurTime;
        ent.Comp.DelayStartTime = curTime;
        ent.Comp.DelayEndTime = curTime - _metadata.GetPauseTime(ent) + ent.Comp.Delay;
        Dirty(ent);
        return true;
    }

    public bool TryResetDelay(EntityUid uid, bool checkDelayed = false, UseDelayComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return false;

        return TryResetDelay((uid, component), checkDelayed);
    }
}
