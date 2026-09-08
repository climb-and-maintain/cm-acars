using System.Windows;
using System.Windows.Controls;

namespace ClimbAndMaintain.Acars.App.Behaviors;

public static class PasswordBoxBinding
{
    public static readonly DependencyProperty BoundPasswordProperty = DependencyProperty.RegisterAttached(
        "BoundPassword",
        typeof(string),
        typeof(PasswordBoxBinding),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBoundPasswordChanged));

    private static readonly DependencyProperty UpdatingProperty = DependencyProperty.RegisterAttached(
        "Updating",
        typeof(bool),
        typeof(PasswordBoxBinding));

    public static string GetBoundPassword(DependencyObject element) =>
        (string)element.GetValue(BoundPasswordProperty);

    public static void SetBoundPassword(DependencyObject element, string value) =>
        element.SetValue(BoundPasswordProperty, value);

    private static void OnBoundPasswordChanged(DependencyObject sender, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (sender is not PasswordBox passwordBox)
        {
            return;
        }

        passwordBox.PasswordChanged -= OnPasswordChanged;
        if (!(bool)passwordBox.GetValue(UpdatingProperty))
        {
            passwordBox.Password = eventArgs.NewValue as string ?? string.Empty;
        }

        passwordBox.PasswordChanged += OnPasswordChanged;
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs eventArgs)
    {
        PasswordBox passwordBox = (PasswordBox)sender;
        passwordBox.SetValue(UpdatingProperty, true);
        SetBoundPassword(passwordBox, passwordBox.Password);
        passwordBox.SetValue(UpdatingProperty, false);
    }
}
