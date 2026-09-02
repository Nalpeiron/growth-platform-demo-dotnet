window.nalpeironFastSpring = {
    openPopupCheckout: function (storefront, sessionId, productPaths, orderTags, returnUrl, requireOrderReference) {
        const setStatus = function (message) {
            const status = document.getElementById('fastspring-checkout-status');
            if (status) {
                status.textContent = message;
            }
        };
        const selectedProducts = Array.isArray(productPaths)
            ? productPaths.filter(productPath => typeof productPath === 'string' && productPath.length > 0)
            : [];
        const tags = Object.fromEntries(
            Object.entries(orderTags || {}).filter(([, value]) => typeof value === 'string' && value.length > 0));

        window.nalpeironFastSpringDataCallback = function (data) {
            console.debug('FastSpring data callback', data);
        };
        window.nalpeironFastSpringErrorCallback = function (data) {
            console.error('FastSpring error callback', data);
            setStatus('FastSpring checkout could not be opened. Return to checkout and try again.');
        };
        window.nalpeironFastSpringPopupClosed = function (orderReference) {
            console.debug('FastSpring popup closed', orderReference);
            if (orderReference) {
                const returnTarget = new URL(returnUrl, window.location.origin);
                const providerOrderRefId =
                    orderReference.id ||
                    orderReference.reference ||
                    orderReference.order ||
                    orderReference.orderReference;
                const providerSubscriptionRefId =
                    orderReference.subscription ||
                    orderReference.subscriptionId ||
                    orderReference.subscriptionReference;
                if (!providerOrderRefId && !providerSubscriptionRefId) {
                    setStatus('Checkout was closed before payment completed. Return to checkout to try again.');
                    return;
                }

                if (requireOrderReference && !providerOrderRefId) {
                    setStatus('FastSpring did not return an order reference. Return to checkout and try again.');
                    return;
                }

                if (providerOrderRefId) {
                    returnTarget.searchParams.set('providerOrderRefId', providerOrderRefId);
                }

                if (providerSubscriptionRefId) {
                    returnTarget.searchParams.set('providerSubscriptionRefId', providerSubscriptionRefId);
                }

                window.location.replace(returnTarget.pathname + returnTarget.search);
                return;
            }

            setStatus('Checkout was closed before payment completed. Return to checkout to try again.');
        };

        const existing = document.getElementById('fsc-api');
        if (existing) {
            existing.remove();
        }

        window.fastspring = undefined;
        const script = document.createElement('script');
        script.id = 'fsc-api';
        script.src = 'https://sbl.onfastspring.com/sbl/1.0.7/fastspring-builder.min.js';
        script.type = 'text/javascript';
        script.setAttribute('data-storefront', storefront);
        script.setAttribute('data-debug', 'true');
        script.setAttribute('data-data-callback', 'nalpeironFastSpringDataCallback');
        script.setAttribute('data-error-callback', 'nalpeironFastSpringErrorCallback');
        script.setAttribute('data-popup-closed', 'nalpeironFastSpringPopupClosed');
        script.onerror = function () {
            console.error('FastSpring Store Builder script failed to load', {storefront, sessionId, selectedProducts});
            setStatus('FastSpring Store Builder failed to load. Return to checkout and try again.');
        };
        script.onload = function () {
            if (window.fastspring && window.fastspring.builder) {
                console.debug('Opening FastSpring popup checkout', {storefront, sessionId, selectedProducts, tags});
                if (selectedProducts.length === 0) {
                    setStatus('No FastSpring products were selected. Return to checkout and try again.');
                    return;
                }

                const addProduct = function (index) {
                    if (index >= selectedProducts.length) {
                        window.fastspring.builder.checkout();
                        return;
                    }

                    window.fastspring.builder.add(selectedProducts[index], function () {
                        addProduct(index + 1);
                    });
                };

                window.fastspring.builder.reset();
                window.fastspring.builder.tag(tags);
                addProduct(0);
            } else {
                console.error('FastSpring Store Builder loaded without a builder API', {storefront, sessionId, selectedProducts});
                setStatus('FastSpring Store Builder loaded without checkout support. Return to checkout and try again.');
            }
        };

        document.head.appendChild(script);
    }
};
