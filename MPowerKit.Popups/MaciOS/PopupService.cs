using System.Runtime.CompilerServices;

using CoreGraphics;

using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

using UIKit;

namespace MPowerKit.Popups;

public partial class PopupService
{
    protected static readonly List<UIWindow> PrevKeyWindows = [];

    protected readonly List<UIWindow> Windows = [];

    // The window each popup took key status from. With several windows on screen, popups in
    // different windows close in any order, so each restores its own rather than the last stored.
    private readonly ConditionalWeakTable<PopupPage, UIWindow> _prevKeyWindowByPopup = new();

    protected virtual partial void AttachToWindow(PopupPage page, IViewHandler pageHandler, Window parentWindow)
    {
        var window = parentWindow.Handler.PlatformView as UIWindow
            ?? throw new InvalidOperationException("Window not found");

        var handler = (pageHandler as IPlatformViewHandler)!;

        var gr = new UITapGestureRecognizer(e =>
        {
            var loc = e.LocationOfTouch(0, e.View);
            var view = e.View.HitTest(loc, null);

            if (handler.ViewController is PageViewController pc && pc.CurrentPlatformView == view)
            {
                page.SendBackgroundClick();
            }
        })
        { CancelsTouchesInView = false };

        handler.ViewController!.View!.AddGestureRecognizer(gr);

        // The parent's own scene: with several windows on screen, the first active scene may
        // be another window's.
        var prevKeyWindow = window.WindowScene?.Windows.FirstOrDefault(w => w.IsKeyWindow) ?? GetKeyWindow();

        var action = new DisposableAction(() =>
        {
            handler.ViewController!.View!.RemoveGestureRecognizer(gr);
        });
        page.SetValue(DisposableActionAttached.DisposableActionProperty, action);

        AddToVisualTree(handler, window.WindowScene);

        (prevKeyWindow ?? window).WindowLevel = -1;

        StorePrevKeyWindow(prevKeyWindow);
        if (prevKeyWindow is not null)
        {
            _prevKeyWindowByPopup.AddOrUpdate(page, prevKeyWindow);
        }
    }

    protected virtual void AddToVisualTree(IPlatformViewHandler handler) => AddToVisualTree(handler, null);

    protected virtual void AddToVisualTree(IPlatformViewHandler handler, UIWindowScene? scene)
    {
        var connectedScene = scene ?? GetActiveScene();
        PopupWindow popupWindow = connectedScene is not null
            ? new(connectedScene)
            : new();

        Windows.Add(popupWindow);

        popupWindow.BackgroundColor = UIColor.Clear;

        popupWindow.RootViewController = handler.ViewController;

        popupWindow.MakeKeyAndVisible();
    }

    protected virtual partial void DetachFromWindow(PopupPage page, IViewHandler pageHandler, Window parentWindow)
    {
        var window = parentWindow.Handler.PlatformView as UIWindow
            ?? throw new InvalidOperationException("Window not found");

        var handler = (pageHandler as PageHandler)!;

        var action = page.GetValue(DisposableActionAttached.DisposableActionProperty) as DisposableAction;
        action?.Dispose();

        RemoveFromVisualTree(handler);

        UIWindow? wnd;
        if (_prevKeyWindowByPopup.TryGetValue(page, out var prev))
        {
            _prevKeyWindowByPopup.Remove(page);
            PrevKeyWindows.Remove(prev);
            // The window may have closed while the popup was open.
            wnd = prev.WindowScene is not null ? prev : window;
        }
        else
        {
            wnd = RestorePrevKeyWindow() ?? window;
        }

        wnd.WindowLevel = UIWindowLevel.Normal;

        wnd.MakeKeyWindow();
    }

    protected virtual void RemoveFromVisualTree(IPlatformViewHandler handler)
    {
        var view = handler.ViewController!.View!;

        var popupWindow = view.Window!;

        popupWindow.RootViewController!.DismissViewController(false, null);
        popupWindow.RootViewController.Dispose();
        popupWindow.Hidden = true;
        popupWindow.WindowScene = null;
        popupWindow.Dispose();

        Windows.Remove(popupWindow);
    }

    protected virtual void StorePrevKeyWindow(UIWindow? window)
    {
        if (window is null) return;

        PrevKeyWindows.Remove(window);

        PrevKeyWindows.Add(window);
    }

    protected virtual UIWindow? RestorePrevKeyWindow()
    {
        if (PrevKeyWindows.Count == 0) return null;

        var window = PrevKeyWindows[^1];

        PrevKeyWindows.Remove(window);

        return window;
    }

    public class PopupWindow : UIWindow
    {
        public PopupWindow(IntPtr handle) : base(handle)
        {
        }

        public PopupWindow()
        {
        }

        public PopupWindow(UIWindowScene uiWindowScene) : base(uiWindowScene)
        {
        }

        public override UIView? HitTest(CGPoint point, UIEvent? uievent)
        {
            var viewcontroller = (RootViewController as PageViewController)!;
            var page = (viewcontroller.CurrentView as PopupPage)!;

            var hitTestResult = base.HitTest(point, uievent);

            if (uievent?.Type is not UIEventType.Hover && hitTestResult is not (UITextField or UITextView))
            {
                var responder = viewcontroller.View!.FindFirstResponder();
                if (responder is not (null or MauiDatePicker))
                {
                    responder.EndEditing(true);
                }
            }

            return hitTestResult == viewcontroller.CurrentPlatformView
                && page.BackgroundInputTransparent
                ? null
                : hitTestResult;
        }
    }

    /// <summary>
    /// Finds the MAUI window in the scene that holds the key window, the one the user is
    /// working in when the app has several windows on screen.
    /// </summary>
    private static Window? FindKeySceneWindow()
    {
        var keyScene = UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .FirstOrDefault(scene => scene.ActivationState == UISceneActivationState.ForegroundActive
                && scene.Windows.Any(w => w.IsKeyWindow));
        if (keyScene is null)
        {
            return null;
        }

        return Application.Current?.Windows.LastOrDefault(w =>
            w.Handler?.PlatformView is UIWindow uiWindow && ReferenceEquals(uiWindow.WindowScene, keyScene));
    }

    public static UIWindow? GetKeyWindow()
    {
        var window = GetActiveScene()?.Windows.FirstOrDefault(w => w.IsKeyWindow);

        return window;
    }

    public static UIWindowScene? GetActiveScene()
    {
        var connectedScene = UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .FirstOrDefault(x => x.ActivationState == UISceneActivationState.ForegroundActive);

        return connectedScene;
    }
}

public static class PopupServiceExtensions
{
    public static UIView? FindFirstResponder(this UIView view)
    {
        if (view.IsFirstResponder) return view;

        foreach (var subView in view.Subviews)
        {
            var responder = subView.FindFirstResponder();
            if (responder is not null) return responder;
        }

        return null;
    }
}