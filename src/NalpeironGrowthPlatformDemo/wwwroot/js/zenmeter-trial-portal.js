(() => {
    let pendingWindow = null;

    const cancel = () => {
        if (pendingWindow && !pendingWindow.closed) {
            pendingWindow.close();
        }
        pendingWindow = null;
    };

    // Blazor Server prepares the authenticated URL asynchronously. Reserve the window
    // during the actual browser click so popup blockers do not treat it as unsolicited.
    document.addEventListener('click', event => {
        const button = event.target.closest?.('button[data-zenmeter-trial-portal]');
        if (!button || button.disabled || pendingWindow && !pendingWindow.closed) {
            return;
        }
        pendingWindow = window.open('about:blank', '_blank', 'popup,width=1100,height=850');
        if (pendingWindow) {
            pendingWindow.opener = null;
        }
    }, true);

    window.nalpeironTrialPortal = {
        navigate: url => {
            const target = new URL(url);
            if (target.protocol !== 'https:' || target.username || target.password) {
                cancel();
                return false;
            }
            if (!pendingWindow || pendingWindow.closed) {
                pendingWindow = null;
                return false;
            }
            pendingWindow.location.replace(target.href);
            // Once handed to FastSpring, leave the buyer's window open independently.
            pendingWindow = null;
            return true;
        },
        cancel
    };
})();
