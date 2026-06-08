namespace EveDashboard.App.Wpf.Reporting;

using System.Windows.Controls;

/// <summary>
/// Plan 34 (revised) — render-thread-animated loading spinner. A static
/// asteroid image sits in the centre; a mining-barge image orbits around it
/// continuously via a <c>RotateTransform</c> animation on a parent container.
/// Because <c>RotateTransform</c> is a <c>Freezable</c> and its <c>Angle</c>
/// is a <c>DependencyProperty</c>, the WPF composition (render) thread owns
/// the animation — the spinner keeps animating smoothly even when the UI
/// dispatcher is fully blocked by binding-update cascades during a load.
/// </summary>
/// <remarks>
/// This replaces the earlier <c>SpriteAtlasSpinner</c> approach, which used
/// a <c>DispatcherTimer</c> on the UI thread to cycle atlas frames. That
/// timer fired at <c>DispatcherPriority.Background</c> (priority 4) and got
/// starved by higher-priority dispatcher work during loads (priority 7-9),
/// freezing the spinner mid-load. Render-thread animation has no such
/// dependency on the dispatcher.
/// <para>
/// The ship's PNG faces RIGHT (laser-emitter side on the right edge). A
/// per-image <c>RotateTransform(Angle=180)</c> in XAML flips it so the lasers
/// face INWARD toward the asteroid at the 3-o'clock orbital position; the
/// outer <c>OrbitRotate</c> rotation then carries this base orientation
/// around the orbit, keeping the lasers pointed at the asteroid at every
/// position.
/// </para>
/// </remarks>
public partial class OrbitalSpinner : UserControl
{
    /// <summary>Construct the orbital spinner. XAML wires the Storyboard via the Loaded event trigger.</summary>
    public OrbitalSpinner()
    {
        InitializeComponent();
    }
}
