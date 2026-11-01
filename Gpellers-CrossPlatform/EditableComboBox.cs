using System;
using System.Collections;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Gpellers;

/// <summary>
/// Выпадающий список с возможностью как выбора значения из списка (presets),
/// так и свободного ввода произвольного пользовательского значения в текстовое поле.
/// Полный аналог ComboBox со стилем DropDown из WinForms.
/// </summary>
public class EditableComboBox : UserControl
{
    private readonly TextBox _textBox;
    private readonly Border _dropButton;
    private readonly Popup _popup;
    private readonly ListBox _listBox;
    private readonly Border _popupBorder;
    private bool _isUpdating = false;
    private DateTime _lastClosedTime = DateTime.MinValue;

    public event EventHandler? SelectionChanged;
    public event EventHandler<TextChangedEventArgs>? TextChanged;

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<EditableComboBox, string>(nameof(Text), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<EditableComboBox, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<object?> SelectedItemProperty =
        AvaloniaProperty.Register<EditableComboBox, object?>(nameof(SelectedItem), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<EditableComboBox, int>(nameof(SelectedIndex), -1, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<string?> WatermarkProperty =
        AvaloniaProperty.Register<EditableComboBox, string?>(nameof(Watermark));

    public string Text
    {
        get => GetValue(TextProperty) ?? _textBox.Text ?? "";
        set
        {
            SetValue(TextProperty, value);
            if (_textBox.Text != value)
            {
                _textBox.Text = value;
            }
        }
    }

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public string? Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    public TextBox InnerTextBox => _textBox;

    public EditableComboBox()
    {
        // Кнопка-стрелочка реализована через Border (а не Button),
        // чтобы избежать перехвата и подавления событий мыши встроенными обработчиками Button.OnPointerPressed!
        _dropButton = new Border
        {
            Width = 28,
            Background = Brushes.Transparent, // Прозрачный фон обязателен для получения кликов
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock
            {
                Text = "▼",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.Parse("#555555")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false // Клик попадает строго в Border
            }
        };

        // Визуальный отклик при наведении на стрелку
        _dropButton.PointerEntered += (s, e) =>
        {
            _dropButton.Background = new SolidColorBrush(Color.FromArgb(25, 0, 0, 0));
        };
        _dropButton.PointerExited += (s, e) =>
        {
            _dropButton.Background = Brushes.Transparent;
        };

        _textBox = new TextBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            MinHeight = 32,
            InnerRightContent = _dropButton,
            Padding = new Thickness(8, 5, 2, 5)
        };

        _listBox = new ListBox
        {
            Background = Brushes.White,
            BorderThickness = new Thickness(0),
            MaxHeight = 220
        };
        ScrollViewer.SetVerticalScrollBarVisibility(_listBox, ScrollBarVisibility.Auto);

        // Стиль элементов списка с комфортными отступами
        _listBox.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(ListBoxItem.PaddingProperty, new Thickness(10, 6)),
                new Setter(ListBoxItem.MinHeightProperty, 28.0)
            }
        });

        _popupBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.Parse("#C0C0C0")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 8, Color = Color.FromArgb(40, 0, 0, 0), OffsetY = 3 }),
            Padding = new Thickness(1),
            Child = _listBox
        };

        _popup = new Popup
        {
            PlacementTarget = _textBox,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            IsLightDismissEnabled = true,
            Child = _popupBorder
        };

        // Запоминаем время закрытия попапа для защиты от случайного повторного открытия кликом
        _popup.Closed += (s, e) =>
        {
            _lastClosedTime = DateTime.UtcNow;
        };

        var container = new Panel();
        container.Children.Add(_textBox);
        container.Children.Add(_popup);
        Content = container;

        // Открытие / закрытие выпадающего списка МГНОВЕННО по первому нажатию мыши на стрелку
        _dropButton.PointerPressed += (s, e) =>
        {
            if (e.GetCurrentPoint(_dropButton).Properties.IsLeftButtonPressed)
            {
                e.Handled = true; // Предотвращаем передачу клика в TextBox
                ToggleDropdown();
            }
        };

        // Двойной клик по текстовому полю также открывает список вариантов
        _textBox.DoubleTapped += (s, e) =>
        {
            ToggleDropdown();
        };

        // Выбор нового элемента из выпадающего списка
        _listBox.SelectionChanged += (s, e) =>
        {
            if (_isUpdating) return;

            if (_listBox.SelectedItem != null)
            {
                _isUpdating = true;
                try
                {
                    string selectedStr = _listBox.SelectedItem.ToString() ?? "";
                    Text = selectedStr;
                    SelectedItem = _listBox.SelectedItem;
                }
                finally
                {
                    _isUpdating = false;
                }

                _popup.IsOpen = false;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        };

        // Клик по уже выбранному элементу в списке также мгновенно закрывает попап
        _listBox.AddHandler(InputElement.PointerReleasedEvent, (s, e) =>
        {
            if (_isUpdating) return;
            _popup.IsOpen = false;
        }, RoutingStrategies.Bubble);

        // Свободный ручной ввод пользовательского значения в текстовое поле
        _textBox.TextChanged += (s, e) =>
        {
            if (!_isUpdating)
            {
                _isUpdating = true;
                try
                {
                    string newText = _textBox.Text ?? "";
                    SetValue(TextProperty, newText);

                    // Проверяем совпадение с одним из пунктов списка
                    object? matchedItem = null;
                    int matchedIndex = -1;
                    if (ItemsSource != null)
                    {
                        int idx = 0;
                        foreach (var item in ItemsSource)
                        {
                            if (item?.ToString()?.Equals(newText, StringComparison.OrdinalIgnoreCase) == true)
                            {
                                matchedItem = item;
                                matchedIndex = idx;
                                break;
                            }
                            idx++;
                        }
                    }

                    SetValue(SelectedItemProperty, matchedItem);
                    SetValue(SelectedIndexProperty, matchedIndex);
                    _listBox.SelectedItem = matchedItem;
                }
                finally
                {
                    _isUpdating = false;
                }

                TextChanged?.Invoke(this, e);
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        };

        // Нажатие стрелки вниз открывает список, Escape закрывает
        _textBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Down)
            {
                ToggleDropdown(true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                _popup.IsOpen = false;
                e.Handled = true;
            }
        };
    }

    private void ToggleDropdown(bool? forceOpen = null)
    {
        if (!forceOpen.HasValue && !_popup.IsOpen)
        {
            // Если попап только что закрылся через LightDismiss по этому же клику, не открываем снова
            if ((DateTime.UtcNow - _lastClosedTime).TotalMilliseconds < 150)
            {
                return;
            }
        }

        _popupBorder.MinWidth = Math.Max(80, _textBox.Bounds.Width);
        bool shouldOpen = forceOpen ?? !_popup.IsOpen;

        if (shouldOpen)
        {
            // Блокируем триггер закрытия попапа во время программной синхронизации выделения!
            _isUpdating = true;
            try
            {
                if (SelectedItem != null)
                {
                    _listBox.SelectedItem = SelectedItem;
                    _listBox.ScrollIntoView(SelectedItem);
                }
            }
            finally
            {
                _isUpdating = false;
            }
        }

        _popup.IsOpen = shouldOpen;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty)
        {
            string? val = change.GetNewValue<string>();
            if (!_isUpdating && _textBox.Text != val)
            {
                _isUpdating = true;
                _textBox.Text = val ?? "";
                _isUpdating = false;
            }
        }
        else if (change.Property == ItemsSourceProperty)
        {
            _listBox.ItemsSource = change.GetNewValue<IEnumerable>();
        }
        else if (change.Property == SelectedItemProperty)
        {
            object? val = change.GetNewValue<object?>();
            if (!_isUpdating && val != null)
            {
                _isUpdating = true;
                try
                {
                    string str = val.ToString() ?? "";
                    _textBox.Text = str;
                    SetValue(TextProperty, str);
                    _listBox.SelectedItem = val;
                }
                finally
                {
                    _isUpdating = false;
                }
            }
        }
        else if (change.Property == SelectedIndexProperty)
        {
            int idx = change.GetNewValue<int>();
            if (!_isUpdating && idx >= 0 && ItemsSource != null)
            {
                int i = 0;
                foreach (var item in ItemsSource)
                {
                    if (i == idx)
                    {
                        SelectedItem = item;
                        break;
                    }
                    i++;
                }
            }
        }
        else if (change.Property == WatermarkProperty)
        {
            _textBox.Watermark = change.GetNewValue<string?>();
        }
    }
}
