package net.majdata.majdataplay.oniimai;

/** USB/UI producers and Unity consumer share one atomic snapshot. No Android dependencies. */
final class InputSnapshot {
    static final long TOUCH_MASK = (1L << 34) - 1;
    private int keyboard, hid, pressedButtons, pressedHid;
    private long touch, pressedTouch, touchAt = -1, hidAt = -1;
    private boolean enabled, foreground = true, panel, awaitNeutral = true, keyboardConnected;
    private boolean waitingTouch, waitingHid;

    synchronized void enabled(boolean value) { enabled = value; resetGate(); }
    synchronized void foreground(boolean value) { foreground = value; resetGate(); }
    synchronized void panel(boolean value) { panel = value; resetGate(); }
    synchronized void awaitStreams(boolean touch, boolean hid) { waitingTouch = touch; waitingHid = hid; resetGate(); }
    private void resetGate() { pressedButtons = pressedHid = 0; pressedTouch = 0; awaitNeutral = true; }
    private boolean active() { return enabled && foreground && !panel; }
    private void changed(int oldButtons, long oldTouch, boolean fromHid) {
        int buttons = keyboard | hid;
        if (!active()) { resetGate(); return; }
        if (awaitNeutral) {
            if (!waitingTouch && !waitingHid && buttons == 0 && touch == 0) awaitNeutral = false;
            return;
        }
        if (fromHid) pressedHid |= buttons & ~oldButtons;
        else pressedButtons |= buttons & ~oldButtons;
        long risingTouch = touch & ~oldTouch;
        // The game has one center zone: moving a finger from C1 to C2 is not another click.
        if ((oldTouch & (3L << 16)) != 0) risingTouch &= ~(3L << 16);
        pressedTouch |= risingTouch;
    }
    synchronized void keyboard(int mask, boolean connected) {
        int before = keyboard | hid;
        keyboard = mask & 511; keyboardConnected = connected;
        changed(before, touch, false);
    }
    synchronized void hid(int mask, long now) {
        int before = keyboard | hid;
        hid = mask & 511; hidAt = now; waitingHid = false; changed(before, touch, true);
    }
    synchronized void touch(long mask, long now) {
        long before = touch;
        touch = mask & TOUCH_MASK; touchAt = now; waitingTouch = false; changed(keyboard | hid, before, false);
    }
    synchronized void disconnect() {
        touch = 0; hid = 0; keyboard = 0; touchAt = hidAt = -1; waitingTouch = waitingHid = false; resetGate();
    }
    private void expire(long now) {
        if (touchAt >= 0 && now - touchAt > 500) {
            if (touch != 0) { resetGate(); waitingTouch = true; }
            touch = 0; pressedTouch = 0; touchAt = -1;
        }
        if (hidAt >= 0 && now - hidAt > 500) {
            if (hid != 0) { resetGate(); waitingHid = true; }
            hid = 0; pressedHid = 0; hidAt = -1;
        }
    }
    /** held-or-pulse buttons, held-or-pulse raw touch, button edges, touch edges, connection flags. */
    synchronized long[] read(long now) {
        expire(now);
        if (awaitNeutral && active() && !waitingTouch && !waitingHid && (keyboard | hid) == 0 && touch == 0) awaitNeutral = false;
        boolean send = active() && !awaitNeutral;
        long status = (touchAt >= 0 ? 1 : 0) | (hidAt >= 0 || keyboardConnected ? 2 : 0);
        long[] result = {send ? (keyboard | hid | pressedButtons | pressedHid) : 0,
            send ? touch | pressedTouch : 0, send ? pressedButtons | pressedHid : 0, send ? pressedTouch : 0, status};
        pressedButtons = pressedHid = 0; pressedTouch = 0;
        return result;
    }
    synchronized long[] diagnostic(long now) {
        expire(now);
        return new long[]{keyboard | hid, touch, awaitNeutral ? 1 : 0,
                (touchAt >= 0 ? 1 : 0) | (hidAt >= 0 || keyboardConnected ? 2 : 0)};
    }
}
