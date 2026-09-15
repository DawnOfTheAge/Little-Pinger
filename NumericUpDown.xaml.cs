using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LittlePinger;

/// <summary>
/// A simple integer numeric up-down control styled for the dark theme.
/// Exposes <see cref="Value"/>, <see cref="Minimum"/>, <see cref="Maximum"/>,
/// and <see cref="Step"/> dependency properties for two-way data binding.
/// </summary>
public partial class NumericUpDown : UserControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(int), typeof(NumericUpDown),
            new FrameworkPropertyMetadata(0,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnValueChanged));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(int), typeof(NumericUpDown),
            new PropertyMetadata(0));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(int), typeof(NumericUpDown),
            new PropertyMetadata(int.MaxValue));

    public static readonly DependencyProperty StepProperty =
        DependencyProperty.Register(nameof(Step), typeof(int), typeof(NumericUpDown),
            new PropertyMetadata(1));

    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, Clamp(value));
    }

    public int Minimum
    {
        get => (int)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public int Maximum
    {
        get => (int)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public int Step
    {
        get => (int)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public NumericUpDown()
    {
        InitializeComponent();
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Nothing extra needed — the TextBox binding handles display updates.
    }

    private void UpBtn_Click(object sender, RoutedEventArgs e)   => Value = Clamp(Value + Step);
    private void DownBtn_Click(object sender, RoutedEventArgs e) => Value = Clamp(Value - Step);

    private void ValueBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // Only allow digits (and an optional leading minus)
        e.Handled = !e.Text.All(c => char.IsDigit(c) || (c == '-' && ValueBox.CaretIndex == 0));
    }

    private void ValueBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(ValueBox.Text, out int v))
            Value = Clamp(v);
        else
            ValueBox.Text = Value.ToString(); // revert to last valid value
    }

    private int Clamp(int v) => v < Minimum ? Minimum : v > Maximum ? Maximum : v;
}
