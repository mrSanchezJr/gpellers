using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Gpellers;

/// <summary>
/// =============================================================================
/// ASIS-2AX-V (Gpellers) - Кроссплатформенный контроллер ЧПУ (Windows / Linux)
/// =============================================================================
/// Монолитный Code-Behind контроллер. Полностью сохранена архитектура оригинала
/// из WinForms без избыточных ООП-усложнений.
/// 
/// КЛЮЧЕВЫЕ ОТЛИЧИЯ ОТ G-WRITE (ASIS-3AX):
/// 1. В блоке groupBox4 вместо оси Z находится "Подача воды 💧" (насос дозирования):
///    - buttonUpZ: Подача воды вперед: G01 Z{stepHandZ} F{speedWater}
///    - buttonDownZ: Откачка/реверс: G01 Z-{stepHandZ} F{speedWater}
///    - comboBox7: Скорость подачи воды (speedWater, мл/с)
///    - comboBox5: Объём дозирования (stepHandZ, мл)
/// 2. В блоке groupBox2 (XY) добавлена кнопка "Дом" (button13: поиск баз по $H).
/// 3. В плане сканирования gen():
///    - Учитывается время первичной прокачки воды (comboBox6, start_w).
///    - На каждый шаг растра координата Z инкрементируется переменной timeWater!
///    - Формат заголовка файла: x1, y1, x2, y2, wtm (start_w), spd, stp.
/// =============================================================================
/// </summary>
public partial class MainWindow : Window
{
    // =========================================================================
    // ПЕРЕМЕННЫЕ И ПОЛЯ (Сохранены 1:1 из оригинального Form1.cs)
    // =========================================================================
    string stepHandZ = "";
    float timeWater = 1;
    float start_w;
    private bool isCounterCommand = false;
    double x, y, z;
    string sx = "0.0", sy = "0.0", sz = "0.0";
    private string lastSx = "", lastSy = "", lastSz = "";

    int i = 0;
    int j = 0;
    float length = 0;
    private TaskCompletionSource<string>? responseTcs;
    public string receiveOk = "";
    public string boxtext = "";
    public bool timeIsReady = false;
    public int DeviceConnectAttempts = 0;
    float xStart = 0, yStart = 0, xEnd = 0, yEnd = 0, step = 0;
    bool isConnected = false;
    string[] ports = Array.Empty<string>();
    string[] steps = ["10", "20", "50", "60"];
    string[] speeds = ["50", "100", "200", "400"];
    string stepHand = "10";
    string speedHand = (10 * 60).ToString();
    string speedWater = "10";

    // Подключение к ЧПУ: кроссплатформенная абстракция ICNCConnection (Serial / TCP Wi-Fi)
    private ICNCConnection? port;
    private ConnectionConfig connectionConfig = ConnectionConfig.Load();

    public char logReceive;
    public string receiveData = "";
    public string str = "";
    public string currentX = "0.0";
    public string currentY = "0.0";
    public string currentZ = "0.0";
    public string currentX_str = "0.0";
    public string currentY_str = "0.0";
    public bool show = false;
    public bool sendDone = false;
    public bool isNotPaused = true;
    public bool isNotStop = true;
    public bool isNotStopCounter = true;

    // Флаг активной потоковой передачи G-кода
    public bool isStreaming = false;

    // Ограничение кольцевого буфера терминала
    private const int MaxTerminalLines = 300;

    public bool ok = true;
    string[] dataToSend = Array.Empty<string>();
    private readonly object tcsLock = new object();

    // Экранная клавиатура Numpad для сенсорных экранов
    private NumpadWindow? numpadWindow;
    private TextBox? activeTextBox;

    // Путь к временному файлу траектории
    string tempPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp.txt");

    // Таймер периодического опроса координат (аналог timer1 в WinForms)
    private DispatcherTimer? timer1;

    // =========================================================================
    // КОНСТРУКТОР ОКНА
    // =========================================================================
    public MainWindow()
    {
        InitializeComponent();

        // Отображение актуальной версии из сборки (Gpellers.csproj)
        label12.Text = AppVersion.LabelString;

        // Инициализация выпадающих списков
        InitComboBoxes();

        // Загрузка настроек соединения (COM / TCP)
        InitConnectionSettings();

        // Инициализация экранной клавиатуры Numpad
        InitializeNumpad();

        // Подписка на глобальный фокус для Numpad
        AddHandler(InputElement.GotFocusEvent, OnGlobalGotFocus, RoutingStrategies.Bubble);

        // Подписка на клавиатурные стрелки для управления джогом
        KeyDown += MainWindow_KeyDown;

        // Запуск таймера опроса статуса контроллера (100 мс)
        InitTimer();

        // Корректное закрытие ресурсов при выходе
        Closing += MainWindow_Closing;
    }

    // =========================================================================
    // ИНИЦИАЛИЗАЦИЯ ВЫПАДАЮЩИХ СПИСКОВ
    // =========================================================================
    private void InitComboBoxes()
    {
        // Шаг сканирования (comboBox2)
        comboBox2.ItemsSource = new string[] { "0.1", "1", "2" };
        comboBox2.SelectedIndex = 1; // 1 мм по умолчанию

        // Скорость ручного перемещения (comboBox3)
        comboBox3.ItemsSource = steps; // 10, 20, 50, 60 мм/с
        comboBox3.SelectedIndex = 0; // 10 мм/с

        // Скорость сканирования (comboBox4)
        comboBox4.ItemsSource = speeds; // 50, 100, 200, 400 мм/с
        comboBox4.SelectedIndex = 0; // 50 мм/с

        // Объём дозирования воды (comboBox5)
        comboBox5.ItemsSource = new string[] { "1", "10", "50", "100" };
        comboBox5.SelectedIndex = 1; // 10 мл

        // Время первичной прокачки воды (comboBox6)
        comboBox6.ItemsSource = new string[] { "10", "20", "100", "200" };
        comboBox6.SelectedIndex = 0; // 10 сек

        // Скорость подачи воды (comboBox7)
        comboBox7.ItemsSource = new string[] { "10", "20", "100", "200" };
        comboBox7.SelectedIndex = 0; // 10 мл/с
    }

    // =========================================================================
    // ТЕРМИНАЛ: ВЫВОД ЛОГА С ОГРАНИЧЕНИЕМ СТРОК
    // =========================================================================
    private void AppendLog(string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (box == null) return;
            string current = box.Text ?? "";
            string[] lines = current.Split('\n');
            if (lines.Length > MaxTerminalLines)
            {
                // Удаляем первые 80 строк для предотвращения утечки памяти
                current = string.Join('\n', lines.Skip(80));
            }
            box.Text = current + text + "\n";
            // Автоматическая прокрутка в конец
            box.CaretIndex = box.Text.Length;
        });
    }

    // =========================================================================
    // НАСТРОЙКИ ПОДКЛЮЧЕНИЯ (COM / TCP)
    // =========================================================================
    private void InitConnectionSettings()
    {
        RefreshComPorts();

        txtTcpIp.Text = string.IsNullOrEmpty(connectionConfig.TcpHost) ? "192.168.4.1" : connectionConfig.TcpHost;
        txtTcpPort.Text = connectionConfig.TcpPort.ToString();

        if (connectionConfig.IsTcpMode)
        {
            rbTcp.IsChecked = true;
            SwitchConnectionMode(true);
        }
        else
        {
            rbSerial.IsChecked = true;
            SwitchConnectionMode(false);
        }
    }

    private void rbSerial_CheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (rbSerial.IsChecked == true)
        {
            SwitchConnectionMode(false);
        }
    }

    private void rbTcp_CheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (rbTcp.IsChecked == true)
        {
            SwitchConnectionMode(true);
        }
    }

    private void SwitchConnectionMode(bool isTcp)
    {
        if (pnlTcp != null && pnlSerial != null)
        {
            pnlTcp.IsVisible = isTcp;
            pnlSerial.IsVisible = !isTcp;
        }

        connectionConfig.IsTcpMode = isTcp;
        connectionConfig.Save();
    }

    private void RefreshComPorts()
    {
        ports = SerialCNCConnection.GetAvailablePorts();
        comboBox1.ItemsSource = ports;
        if (ports.Length > 0)
        {
            if (!string.IsNullOrEmpty(connectionConfig.LastComPort) && ports.Contains(connectionConfig.LastComPort))
            {
                comboBox1.SelectedItem = connectionConfig.LastComPort;
            }
            else
            {
                comboBox1.SelectedIndex = 0;
            }
        }
    }

    // =========================================================================
    // ПОДКЛЮЧЕНИЕ / ОТКЛЮЧЕНИЕ ОТ ЧПУ КОНТРОЛЛЕРА
    // =========================================================================
    private void button1_Click(object? sender, RoutedEventArgs e)
    {
        if (!isConnected)
        {
            connect();
        }
        else
        {
            disconnect();
        }
    }

    private void connect()
    {
        try
        {
            bool isTcp = rbTcp.IsChecked == true;

            if (isTcp)
            {
                string ip = txtTcpIp.Text?.Trim() ?? "192.168.4.1";
                if (!int.TryParse(txtTcpPort.Text?.Trim(), out int tcpPortVal))
                {
                    tcpPortVal = 8088;
                }
                connectionConfig.TcpHost = ip;
                connectionConfig.TcpPort = tcpPortVal;
                connectionConfig.Save();

                AppendLog($"[Подключение] Wi-Fi TCP: {ip}:{tcpPortVal}...");
                port = new TcpCNCConnection(ip, tcpPortVal);
            }
            else
            {
                string comName = comboBox1.Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(comName))
                {
                    AppendLog("[Ошибка] COM-порт не выбран!");
                    return;
                }
                connectionConfig.LastComPort = comName;
                connectionConfig.Save();

                AppendLog($"[Подключение] Последовательный порт: {comName}...");
                port = new SerialCNCConnection(comName, 115200);
            }

            port.OnLineReceived += HandleIncomingLine;
            port.OnConnectionStatusChanged += (status) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (!status && isConnected)
                    {
                        disconnect();
                    }
                });
            };

            port.Open();
            isConnected = true;
            button1.Content = "Отключить";
            lblConnStatus.Text = "Подключено";
            lblConnStatus.Foreground = Brushes.ForestGreen;
            AppendLog("[Успех] Соединение с контроллером установлено!");
        }
        catch (Exception ex)
        {
            isConnected = false;
            button1.Content = "Подключить";
            lblConnStatus.Text = "Ошибка";
            lblConnStatus.Foreground = Brushes.Crimson;
            AppendLog($"[Ошибка подключения] {ex.Message}");
        }
    }

    private void disconnect()
    {
        try
        {
            port?.Close();
            port = null;
        }
        catch { }
        finally
        {
            isConnected = false;
            button1.Content = "Подключить";
            lblConnStatus.Text = "Отключено";
            lblConnStatus.Foreground = Brushes.DimGray;
            isNotStop = true;
            isNotPaused = true;
            UpdatePauseButton(false);
            if (txtButton10 != null) txtButton10.Text = "СТОП";
            if (txtIcon10 != null) txtIcon10.Text = "🛑";
            if (button10 != null) button10.Background = new SolidColorBrush(Color.Parse("#D9534F"));
            AppendLog("[Инфо] Отключено от контроллера.");
        }
    }

    // =========================================================================
    // ОБРАБОТКА ВХОДЯЩИХ СТРОК ОТ КОНТРОЛЛЕРА (GRBL / INECTRA)
    // =========================================================================
    private void HandleIncomingLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        line = line.Trim();

        // 1. Разбор статусных сообщений GRBL вида: <Idle|MPos:...> или <Idle|WPos:...>
        if (line.StartsWith("<"))
        {
            ParseGrblStatus(line);
            return;
        }

        // 2. Игнорируем эхо команды опроса статуса от таймера (как в ASIS-3AX)
        if (isCounterCommand && line.Equals("ok", StringComparison.OrdinalIgnoreCase))
        {
            isCounterCommand = false;
            return;
        }

        // 3. Вывод остальных ответов в окно монитора
        AppendLog(line);

        // 4. Проверка подтверждения "ok" или ошибки для ожидающего потока (StreamGCodeAsync)
        lock (tcsLock)
        {
            if (responseTcs != null && !responseTcs.Task.IsCompleted)
            {
                if (line.StartsWith("ok", StringComparison.OrdinalIgnoreCase) || 
                    line.StartsWith("error", StringComparison.OrdinalIgnoreCase))
                {
                    responseTcs.TrySetResult(line);
                }
            }
        }
    }

    private void ParseGrblStatus(string statusLine)
    {
        try
        {
            string body = statusLine.Trim('<', '>');
            string[] parts = body.Split('|');

            foreach (var part in parts)
            {
                if (part.StartsWith("MPos:") || part.StartsWith("WPos:"))
                {
                    string coordsPart = part.Substring(5);
                    string[] coords = coordsPart.Split(',');
                    if (coords.Length >= 2)
                    {
                        sx = coords[0];
                        sy = coords[1];
                        if (coords.Length >= 3)
                        {
                            sz = coords[2];
                        }

                        // Обновляем UI только если координаты изменились
                        if (sx != lastSx || sy != lastSy || sz != lastSz)
                        {
                            lastSx = sx;
                            lastSy = sy;
                            lastSz = sz;

                            currentX = sx;
                            currentY = sy;
                            currentZ = sz;

                            Dispatcher.UIThread.Post(() =>
                            {
                                label17.Text = $"X: {sx}";
                                label18.Text = $"Y: {sy}";
                                label19.Text = $"Z: {sz}";
                            });
                        }
                    }
                    break;
                }
            }
        }
        catch { }
    }

    // =========================================================================
    // ТАЙМЕР ОПРОСА СТАТУСА (100 мс)
    // =========================================================================
    public void InitTimer()
    {
        timer1 = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        timer1.Tick += timer1_Tick;
        timer1.Start();
    }

    private void timer1_Tick(object? sender, EventArgs e)
    {
        try
        {
            if (isConnected && port != null && port.IsOpen)
            {
                isCounterCommand = true;
                port.WriteLine("?");
            }
        }
        catch { }
    }

    // =========================================================================
    // РУЧНАЯ ОТПРАВКА КОМАНД И ДЖОГ
    // =========================================================================
    public void WriteIt(string toWrite, string prefix)
    {
        try
        {
            if (port != null && port.IsOpen)
            {
                isCounterCommand = true;
                if (!string.IsNullOrEmpty(prefix))
                {
                    port.Write(prefix + "\n");
                    AppendLog(prefix);
                }
                port.Write(toWrite + "\n");
                AppendLog(toWrite);
                isCounterCommand = false;
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[Ошибка отправки] {ex.Message}");
            isCounterCommand = false;
        }
    }

    private void customBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)
        {
            Button4_Click(sender, new RoutedEventArgs());
        }
    }

    private void Button4_Click(object? sender, RoutedEventArgs e)
    {
        string cmd = customBox.Text?.Trim() ?? "";
        if (!string.IsNullOrEmpty(cmd))
        {
            WriteIt(cmd, "");
            customBox.Text = "";
        }
    }

    private void button2_Click(object? sender, RoutedEventArgs e)
    {
        RefreshComPorts();
    }

    private void button5_Click(object? sender, RoutedEventArgs e)
    {
        box.Text = "";
    }

    // =========================================================================
    // ДЖОГ ПЛОСКОСТИ X/Y
    // =========================================================================
    private void buttonUpXY_Click(object? sender, RoutedEventArgs e)
    {
        WriteIt($"G01 Y{stepHand} F{speedHand}", "G91");
    }

    private void buttonDownXY_Click(object? sender, RoutedEventArgs e)
    {
        WriteIt($"G01 Y-{stepHand} F{speedHand}", "G91");
    }

    private void buttonLeftXY_Click(object? sender, RoutedEventArgs e)
    {
        WriteIt($"G01 X-{stepHand} F{speedHand}", "G91");
    }

    private void buttonRightXY_Click(object? sender, RoutedEventArgs e)
    {
        WriteIt($"G01 X{stepHand} F{speedHand}", "G91");
    }

    private void buttonHomeXY_Click(object? sender, RoutedEventArgs e)
    {
        WriteIt("G90\nG01 X0 Y0 F" + speedHand, "");
    }

    // =========================================================================
    // КНОПКА 'ДОМ' ($H) - СПЕЦИФИКА GPELLERS (button13)
    // =========================================================================
    private void button13_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            port?.Write("$H\n");
            isCounterCommand = false;
            AppendLog("$H");
        }
        catch { }
    }

    // =========================================================================

    // =========================================================================
    // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ДЛЯ РАБОТЫ С COMBOBOX В AVALONIA UI
    // В отличие от WinForms, в Avalonia у ComboBox нет свойства .Text,
    // поэтому значение считывается через SelectedItem / ItemsSource.
    // =========================================================================
    private string GetComboBoxValue(EditableComboBox cb)
    {
        return cb.Text?.Trim().Replace(',', '.') ?? "";
    }

    private void SetComboBoxValue(EditableComboBox cb, string val)
    {
        cb.Text = val;
        cb.SelectedItem = val;
    }

    // ПОДАЧА ВОДЫ 💧 (ОСЬ Z - НАСОС ДОЗИРОВАНИЯ) - СПЕЦИФИКА GPELLERS
    // =========================================================================
    private void buttonUpZ_Click(object? sender, RoutedEventArgs e)
    {
        speedWater = GetComboBoxValue(comboBox7);
        stepHandZ = GetComboBoxValue(comboBox5);
        if (!string.IsNullOrEmpty(speedWater) && !string.IsNullOrEmpty(stepHandZ))
        {
            WriteIt("G01 Z" + stepHandZ + " F" + speedWater, "G91");
        }
        else
        {
            AppendLog("[Предупреждение] Укажите скорость и объём подачи воды!");
        }
        isCounterCommand = false;
    }

    private void buttonDownZ_Click(object? sender, RoutedEventArgs e)
    {
        speedWater = GetComboBoxValue(comboBox7);
        stepHandZ = GetComboBoxValue(comboBox5);
        if (!string.IsNullOrEmpty(speedWater) && !string.IsNullOrEmpty(stepHandZ))
        {
            WriteIt("G01 Z-" + stepHandZ + " F" + speedWater, "G91");
        }
        else
        {
            AppendLog("[Предупреждение] Укажите скорость и объём подачи воды!");
        }
        isCounterCommand = false;
    }

    // =========================================================================
    // ОБНУЛЕНИЕ КООРДИНАТ (СБРОС, button14)
    // =========================================================================
    private void button14_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            port?.Write("G91 Z 0.1\n");
            isCounterCommand = false;
            port?.Write("G91 Z -0.1\n");
            isCounterCommand = false;
            port?.Write("G28.1 X0 Y0 Z0\n");
            isCounterCommand = false;
            port?.Write("G10 L20 P1 X0 Y0 Z0\n");
            isCounterCommand = false;

            AppendLog("G91 Z 0.1");
            AppendLog("G91 Z -0.1");
            AppendLog("G28.1 X0 Y0 Z0");
            AppendLog("G10 L20 P1 X0 Y0 Z0");
        }
        catch { }
    }

    // =========================================================================
    // ПЕРЕКЛЮЧАТЕЛИ ВЕЛИЧИНЫ ШАГА ДЖОГА
    // =========================================================================
    private void radioButton1_CheckedChanged(object? sender, RoutedEventArgs e) { stepHand = "0.1"; }
    private void radioButton2_CheckedChanged(object? sender, RoutedEventArgs e) { stepHand = "1"; }
    private void radioButton4_CheckedChanged(object? sender, RoutedEventArgs e) { stepHand = "5"; }
    private void radioButton3_CheckedChanged(object? sender, RoutedEventArgs e) { stepHand = "10"; }
    private void radioButton5_CheckedChanged(object? sender, RoutedEventArgs e) { stepHand = "50"; }
    private void radioButton6_CheckedChanged(object? sender, RoutedEventArgs e) { stepHand = "100"; }

    private void comboBox3_SelectedIndexChanged(object? sender, EventArgs e)
    {
        string val = comboBox3.Text.Trim();
        if (int.TryParse(val, out int spd))
        {
            speedHand = (spd * 60).ToString();
        }
    }

    // =========================================================================
    // ЗАПИСЬ ТЕКУЩИХ КООРДИНАТ В ТОЧКИ А И В
    // =========================================================================
    private void button6_Click(object? sender, RoutedEventArgs e)
    {
        startPosX.Text = currentX;
        startPosY.Text = currentY;
    }

    private void button7_Click(object? sender, RoutedEventArgs e)
    {
        endPosX.Text = currentX;
        endPosY.Text = currentY;
    }

    // =========================================================================
    // БЕЗОПАСНЫЙ ПАРСИНГ ЧИСЕЛ И ВСПЫШКА КНОПКИ (BlinkButtonAsync)
    // =========================================================================
    private float ParseFloatSafe(string input)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                _ = BlinkButtonAsync(genButton, TimeSpan.FromMilliseconds(250), 3, Brushes.Crimson);
                return 0.987612345f;
            }
            return float.Parse(input.Replace(",", "."), CultureInfo.InvariantCulture);
        }
        catch
        {
            _ = BlinkButtonAsync(genButton, TimeSpan.FromMilliseconds(250), 3, Brushes.Crimson);
            return 0.987612345f;
        }
    }

    private async Task BlinkButtonAsync(Button button, TimeSpan interval, int blinkCount, IBrush firstColor)
    {
        var secondColor = Brushes.White;
        var originalColor = button.Background;

        for (int k = 0; k < blinkCount; k++)
        {
            button.Background = firstColor;
            await Task.Delay(interval);

            button.Background = secondColor;
            await Task.Delay(interval);
        }
        button.Background = originalColor;
    }

    // =========================================================================
    // ГЕНЕРАТОР ТРАЕКТОРИИ СКАНИРОВАНИЯ (gen) - СПЕЦИФИКА GPELLERS С ВОДОЙ
    // =========================================================================
    private void genButton_Click(object? sender, RoutedEventArgs e)
    {
        gen();
    }

    public void gen()
    {
        string spX = startPosX.Text?.Trim() ?? "";
        string spY = startPosY.Text?.Trim() ?? "";
        string epX = endPosX.Text?.Trim() ?? "";
        string epY = endPosY.Text?.Trim() ?? "";
        string cb2 = GetComboBoxValue(comboBox2);
        string cb4 = GetComboBoxValue(comboBox4);
        string cb6 = GetComboBoxValue(comboBox6);

        if (ParseFloatSafe(spX) == 0.987612345f ||
            ParseFloatSafe(spY) == 0.987612345f ||
            ParseFloatSafe(epX) == 0.987612345f ||
            ParseFloatSafe(epY) == 0.987612345f ||
            ParseFloatSafe(cb2) == 0.987612345f ||
            ParseFloatSafe(cb4) == 0.987612345f ||
            ParseFloatSafe(cb6) == 0.987612345f)
        {
            AppendLog("[Ошибка] Заполните все поля плана сканирования!");
            return;
        }

        xStart = ParseFloatSafe(spX);
        yStart = ParseFloatSafe(spY);
        xEnd = ParseFloatSafe(epX);
        yEnd = ParseFloatSafe(epY);
        step = ParseFloatSafe(cb2);
        start_w = ParseFloatSafe(cb6);
        float scanSpeed = ParseFloatSafe(cb4);

        length = 0;

        using (StreamWriter outline = new StreamWriter(tempPath, false, Encoding.UTF8))
        {
            float Xl = xEnd - xStart;
            float Yl = yEnd - yStart;
            int nY = (int)Math.Truncate(Yl / step);
            float tY = (Yl / step - nY) * step;
            int pY = 1;

            outline.WriteLine("G90");
            outline.WriteLine("G17");
            outline.WriteLine("G10 L20 P1 Z0");
            // Первичная прокачка воды перед началом движения:
            outline.WriteLine("G01 Z " + start_w.ToString(CultureInfo.InvariantCulture) + " F " + (scanSpeed * 60).ToString(CultureInfo.InvariantCulture));

            timeWater = start_w;
            timeWater++;

            string speedParam = "F" + (scanSpeed * 60).ToString(CultureInfo.InvariantCulture);

            outline.WriteLine("G01 X " + xStart.ToString(CultureInfo.InvariantCulture) + " Y " + yStart.ToString(CultureInfo.InvariantCulture) + $" Z {timeWater} " + speedParam);
            timeWater++;
            outline.WriteLine("G01 X " + xEnd.ToString(CultureInfo.InvariantCulture) + $" Z {timeWater} " + speedParam);
            timeWater++;
            length += (xEnd - xStart);

            if (yEnd > step)
            {
                while (!(pY > nY))
                {
                    if (!((pY % 2) == 0))
                    {
                        outline.WriteLine("G01 Y " + (pY * step).ToString(CultureInfo.InvariantCulture) + $" Z {timeWater} " + speedParam);
                        timeWater++;
                        outline.WriteLine("G01 X " + xStart.ToString(CultureInfo.InvariantCulture) + $" Z {timeWater} " + speedParam);
                        timeWater++;
                        length += (step + (xEnd - xStart));
                        pY++;
                    }
                    else
                    {
                        outline.WriteLine("G01 Y " + (pY * step).ToString(CultureInfo.InvariantCulture) + $" Z {timeWater} " + speedParam);
                        timeWater++;
                        outline.WriteLine("G01 X " + xEnd.ToString(CultureInfo.InvariantCulture) + $" Z {timeWater} " + speedParam);
                        timeWater++;
                        length += (step + (xEnd - xStart));
                        pY++;
                    }
                }

                if (((yEnd % step) != 0))
                {
                    if (!((pY % 2) == 0))
                    {
                        outline.WriteLine("G01 Y " + (tY + (pY - 1) * step).ToString(CultureInfo.InvariantCulture) + $" Z {timeWater} " + speedParam);
                        timeWater++;
                        outline.WriteLine("G01 X " + xStart.ToString(CultureInfo.InvariantCulture) + $" Z {timeWater} " + speedParam);
                        timeWater++;
                        length += (tY + (xEnd - xStart));
                    }
                    else
                    {
                        outline.WriteLine("G01 Y " + (tY + (pY - 1) * step).ToString(CultureInfo.InvariantCulture) + $" Z {timeWater} " + speedParam);
                        timeWater++;
                        outline.WriteLine("G01 X " + xEnd.ToString(CultureInfo.InvariantCulture) + $" Z {timeWater} " + speedParam);
                        timeWater++;
                        length += (tY + (xEnd - xStart));
                    }
                }
            }
        }

        // Сохранение шапки координат в tempPath
        SaveCoordinates(tempPath, new double[] { xStart, yStart, xEnd, yEnd, start_w, scanSpeed, step });

        if (scanSpeed > 0)
        {
            label16.Text = Math.Round(length / scanSpeed, 1).ToString(CultureInfo.InvariantCulture);
        }

        _ = BlinkButtonAsync(genButton, TimeSpan.FromMilliseconds(250), 3, Brushes.ForestGreen);
        AppendLog("[Генератор] Траектория сканирования с водой успешно построена!");
    }

    // =========================================================================
    // СОХРАНЕНИЕ И ЗАГРУЗКА ШАПКИ КООРДИНАТ
    // =========================================================================
    void SaveCoordinates(string file, double[] coords)
    {
        string header = $"; x1={coords[0].ToString(CultureInfo.InvariantCulture)}\n; y1={coords[1].ToString(CultureInfo.InvariantCulture)}\n; x2={coords[2].ToString(CultureInfo.InvariantCulture)}\n; y2={coords[3].ToString(CultureInfo.InvariantCulture)}\n; wtm={coords[4].ToString(CultureInfo.InvariantCulture)}\n; spd={coords[5].ToString(CultureInfo.InvariantCulture)}\n; stp={coords[6].ToString(CultureInfo.InvariantCulture)}\n\n";
        string existing = File.Exists(file) ? File.ReadAllText(file) : "";
        File.WriteAllText(file, header + existing);
    }

    double[] LoadCoordinates(string file)
    {
        double[] coords = new double[7];
        if (!File.Exists(file)) return coords;

        int idx = 0;
        foreach (string line in File.ReadLines(file))
        {
            if (line.StartsWith(";") && line.Contains("=") && idx < 7)
            {
                double.TryParse(line.Split('=')[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out coords[idx++]);
            }
        }
        return coords;
    }

    // =========================================================================
    // В НАЧАЛО (button12)
    // =========================================================================
    private void button12_Click(object? sender, RoutedEventArgs e)
    {
        double[] loaded = LoadCoordinates(tempPath);
        if (loaded[0] != 0 || loaded[1] != 0)
        {
            xStart = (float)loaded[0];
            yStart = (float)loaded[1];
        }
        else
        {
            xStart = ParseFloatSafe(startPosX.Text ?? "0");
            yStart = ParseFloatSafe(startPosY.Text ?? "0");
        }

        string xCmd = "G01 X" + xStart.ToString(CultureInfo.InvariantCulture) + " F" + speedHand;
        string yCmd = "G01 Y" + yStart.ToString(CultureInfo.InvariantCulture) + " F" + speedHand;

        port?.Write("G90\n");
        isCounterCommand = false;
        AppendLog("G90");

        port?.Write(yCmd + "\n");
        isCounterCommand = false;
        AppendLog(yCmd);

        port?.Write(xCmd + "\n");
        isCounterCommand = false;
        AppendLog(xCmd);
    }

    // =========================================================================
    // ПОТОКОВАЯ ОТПРАВКА G-КОДА (button3)
    // =========================================================================
    private async void button3_Click(object? sender, RoutedEventArgs e)
    {
        if (isStreaming)
        {
            AppendLog("[Инфо] Передача уже выполняется.");
            return;
        }

        if (!File.Exists(tempPath))
        {
            _ = BlinkButtonAsync(button3, TimeSpan.FromMilliseconds(250), 3, Brushes.Crimson);
            AppendLog("[Ошибка] Файл траектории не найден! Сначала нажмите 'Генерировать'.");
            return;
        }

        dataToSend = File.ReadAllLines(tempPath);
        if (dataToSend.Length == 0)
        {
            AppendLog("[Ошибка] Файл траектории пуст!");
            return;
        }

        isStreaming = true;
        isNotStop = true;
        isNotPaused = true;
        button3.IsEnabled = false;

        await StreamGCodeAsync();

        button3.IsEnabled = true;
        isStreaming = false;
        isNotPaused = true;
        UpdatePauseButton(false);
    }

    private async Task StreamGCodeAsync()
    {
        progressBar1.Maximum = dataToSend.Length;
        progressBar1.Value = 0;
        int sentLines = 0;

        foreach (string line in dataToSend)
        {
            if (!isNotStop)
            {
                AppendLog("[Останов] Выполнение программы прервано пользователем.");
                break;
            }

            while (!isNotPaused)
            {
                await Task.Delay(100);
                if (!isNotStop) break;
            }

            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";"))
            {
                sentLines++;
                continue;
            }

            // Ожидание ответа "ok" от контроллера
            lock (tcsLock)
            {
                responseTcs = new TaskCompletionSource<string>();
            }

            port?.Write(trimmed + "\n");
            AppendLog(trimmed);

            // Таймаут ожидания ok 10 секунд
            var completedTask = await Task.WhenAny(responseTcs.Task, Task.Delay(10000));
            if (completedTask != responseTcs.Task)
            {
                AppendLog("[Таймаут] Превышено время ожидания ответа ok.");
            }

            sentLines++;
            progressBar1.Value = sentLines;
            label10.Text = $"Прогресс: {sentLines}/{dataToSend.Length}";
        }

        label10.Text = isNotStop ? "Прогресс: Завершено" : "Прогресс: Остановлено";
        isNotPaused = true;
        UpdatePauseButton(false);
    }

    // Кэш иконок для кнопки Пауза / Продолжить (button11)
    private Bitmap? _iconPause;
    private Bitmap? _iconPlay;

    private void UpdatePauseButton(bool paused)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (paused)
            {
                if (txtButton11 != null) txtButton11.Text = "Продолжить";
                try
                {
                    _iconPlay ??= new Bitmap(AssetLoader.Open(new Uri("avares://Gpellers/Assets/icons8-play-26.png")));
                    if (imgButton11 != null) imgButton11.Source = _iconPlay;
                }
                catch { }
            }
            else
            {
                if (txtButton11 != null) txtButton11.Text = "Пауза";
                try
                {
                    _iconPause ??= new Bitmap(AssetLoader.Open(new Uri("avares://Gpellers/Assets/button11_Image.png")));
                    if (imgButton11 != null) imgButton11.Source = _iconPause;
                }
                catch { }
            }
        });
    }

    // =========================================================================
    // ПАУЗА / ВОЗОБНОВИТЬ (button11)
    // =========================================================================
    private void button11_Click(object? sender, RoutedEventArgs e)
    {
        if (isNotPaused)
        {
            isNotPaused = false;
            timer1?.Stop();
            UpdatePauseButton(true);

            // Байт 0x82: команда GRBL Feed Hold (приостановка подачи)
            byte[] data = new byte[] { 0x82 };
            port?.Write(data, 0, data.Length);
            AppendLog("[Пауза] Программа приостановлена.");
        }
        else
        {
            isNotPaused = true;
            port?.Write("~"); // GRBL Cycle Start (возобновление движения)
            isCounterCommand = false;
            timer1?.Start();
            UpdatePauseButton(false);

            AppendLog("[Возобновление] Продолжение выполнения программы.");
        }
    }

    // =========================================================================
    // АВАРИЙНЫЙ ОСТАНОВ (СТОП, button10)
    // =========================================================================
    private void button10_Click_1(object? sender, RoutedEventArgs e)
    {
        if (port == null || !port.IsOpen) return;

        if (isNotStop)
        {
            isNotStop = false;
            isStreaming = false;
            isNotPaused = true;
            UpdatePauseButton(false);

            if (txtButton10 != null) txtButton10.Text = "РАЗБЛОК.";
            if (txtIcon10 != null) txtIcon10.Text = "🔓";
            if (button10 != null) button10.Background = new SolidColorBrush(Color.Parse("#F57C00"));

            timer1?.Stop();

            // Байт 0x18 (Ctrl-X) — аппаратный сброс GRBL (Soft Reset)
            byte[] cancelCmd = new byte[] { 0x18 };
            port?.Write(cancelCmd, 0, cancelCmd.Length);
            isCounterCommand = false;

            lock (tcsLock)
            {
                responseTcs?.TrySetResult("stop");
            }

            AppendLog("[АВАРИЙНЫЙ СТОП]");
        }
        else
        {
            if (txtButton10 != null) txtButton10.Text = "СТОП";
            if (txtIcon10 != null) txtIcon10.Text = "🛑";
            if (button10 != null) button10.Background = new SolidColorBrush(Color.Parse("#D9534F"));

            port?.Write("$X\n"); // Команда разблокировки станка после аварийного останова ($X)
            isCounterCommand = false;
            timer1?.Start();
            isNotStop = true;

            AppendLog("[Контроллер разблокирован]");
        }
    }

    // =========================================================================
    // СОХРАНЕНИЕ И ЗАГРУЗКА ФАЙЛОВ G-КОДА (button16 / button17)
    // =========================================================================
    private async void button16_Click(object? sender, RoutedEventArgs e)
    {
        if (!File.Exists(tempPath))
        {
            _ = BlinkButtonAsync(button16, TimeSpan.FromMilliseconds(250), 3, Brushes.Crimson);
            AppendLog("[Ошибка] Файл траектории не сгенерирован!");
            return;
        }

        string content = File.ReadAllText(tempPath);
        if (string.IsNullOrWhiteSpace(content))
        {
            _ = BlinkButtonAsync(button16, TimeSpan.FromMilliseconds(250), 3, Brushes.Crimson);
            AppendLog("[Предупреждение] Файл пуст!");
            return;
        }

        var topLevel = GetTopLevel(this);
        if (topLevel != null)
        {
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Сохранить G-code файл",
                DefaultExtension = "gcode",
                SuggestedFileName = "output.gcode",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("G-code файлы") { Patterns = new[] { "*.gcode" } },
                    new FilePickerFileType("Все файлы") { Patterns = new[] { "*.*" } }
                }
            });

            if (file != null)
            {
                try
                {
                    await using var stream = await file.OpenWriteAsync();
                    await using var writer = new StreamWriter(stream, Encoding.UTF8);
                    await writer.WriteAsync(content);
                    _ = BlinkButtonAsync(button16, TimeSpan.FromMilliseconds(250), 3, Brushes.ForestGreen);
                    AppendLog($"[Файл] Успешно сохранено: {file.Name}");
                }
                catch (Exception ex)
                {
                    _ = BlinkButtonAsync(button16, TimeSpan.FromMilliseconds(250), 3, Brushes.Crimson);
                    AppendLog($"[Ошибка сохранения] {ex.Message}");
                }
            }
        }
    }

    private async void button17_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel != null)
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Открыть G-code файл",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("G-code файлы") { Patterns = new[] { "*.gcode" } },
                    new FilePickerFileType("Все файлы") { Patterns = new[] { "*.*" } }
                }
            });

            if (files.Count > 0)
            {
                var file = files[0];
                try
                {
                    await using var stream = await file.OpenReadAsync();
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    string content = await reader.ReadToEndAsync();

                    if (string.IsNullOrWhiteSpace(content))
                    {
                        _ = BlinkButtonAsync(button17, TimeSpan.FromMilliseconds(250), 3, Brushes.Crimson);
                        AppendLog("[Предупреждение] Выбранный файл пуст!");
                        return;
                    }

                    File.WriteAllText(tempPath, content);
                    double[] loaded = LoadCoordinates(tempPath);

                    startPosX.Text = loaded[0].ToString(CultureInfo.InvariantCulture);
                    startPosY.Text = loaded[1].ToString(CultureInfo.InvariantCulture);
                    endPosX.Text = loaded[2].ToString(CultureInfo.InvariantCulture);
                    endPosY.Text = loaded[3].ToString(CultureInfo.InvariantCulture);
                    SetComboBoxValue(comboBox6, loaded[4].ToString(CultureInfo.InvariantCulture)); // wtm (start_w)
                    SetComboBoxValue(comboBox4, loaded[5].ToString(CultureInfo.InvariantCulture)); // spd
                    SetComboBoxValue(comboBox2, loaded[6].ToString(CultureInfo.InvariantCulture)); // stp

                    _ = BlinkButtonAsync(button17, TimeSpan.FromMilliseconds(250), 3, Brushes.ForestGreen);
                    AppendLog($"[Файл] Успешно загружен: {file.Name}");
                }
                catch (Exception ex)
                {
                    _ = BlinkButtonAsync(button17, TimeSpan.FromMilliseconds(250), 3, Brushes.Crimson);
                    AppendLog($"[Ошибка открытия] {ex.Message}");
                }
            }
        }
    }

    // =========================================================================
    // ЭКРАННАЯ ЦИФРОВАЯ КЛАВИАТУРА NUMPAD (ДЛЯ СЕНСОРНЫХ ЭКРАНОВ)
    // =========================================================================
    private void InitializeNumpad()
    {
        numpadWindow = new NumpadWindow();
        numpadWindow.NumberPressed += (s, str) =>
        {
            if (activeTextBox != null)
            {
                int caret = activeTextBox.CaretIndex;
                string text = activeTextBox.Text ?? "";
                activeTextBox.Text = text.Insert(caret, str);
                activeTextBox.CaretIndex = caret + str.Length;
            }
        };
        numpadWindow.BackspacePressed += (s, e) =>
        {
            if (activeTextBox != null && (activeTextBox.Text?.Length ?? 0) > 0)
            {
                int caret = activeTextBox.CaretIndex;
                if (caret > 0)
                {
                    string text = activeTextBox.Text!;
                    activeTextBox.Text = text.Remove(caret - 1, 1);
                    activeTextBox.CaretIndex = caret - 1;
                }
            }
        };
        numpadWindow.ClearPressed += (s, e) =>
        {
            if (activeTextBox != null)
            {
                activeTextBox.Text = "";
            }
        };
        numpadWindow.EnterPressed += (s, e) =>
        {
            // Переход фокуса на следующее поле координат по цепочке: startPosX -> startPosY -> endPosX -> endPosY
            if (activeTextBox == startPosX) startPosY.Focus();
            else if (activeTextBox == startPosY) endPosX.Focus();
            else if (activeTextBox == endPosX) endPosY.Focus();
            else if (activeTextBox == endPosY) startPosX.Focus();
        };
    }

    private void OnGlobalGotFocus(object? sender, GotFocusEventArgs e)
    {
        if (e.Source is TextBox tb && tb != box)
        {
            activeTextBox = tb;
            numpadWindow?.SetTargetField(GetTextBoxFriendlyName(tb));
        }
    }

    private string GetTextBoxFriendlyName(TextBox tb)
    {
        if (tb == startPosX) return "Точка A (X)";
        if (tb == startPosY) return "Точка A (Y)";
        if (tb == endPosX) return "Точка B (X)";
        if (tb == endPosY) return "Точка B (Y)";
        if (tb == customBox) return "G-код консоль";
        if (tb == txtTcpIp) return "TCP IP";
        if (tb == txtTcpPort) return "TCP Порт";
        if (tb == comboBox2.InnerTextBox) return "Шаг (мм)";
        if (tb == comboBox3.InnerTextBox) return "Скорость джога";
        if (tb == comboBox4.InnerTextBox) return "Скорость (мм/с)";
        if (tb == comboBox5.InnerTextBox) return "Объём воды (мл)";
        if (tb == comboBox6.InnerTextBox) return "Время прокачки (с)";
        if (tb == comboBox7.InnerTextBox) return "Скорость воды (мл/с)";
        if (tb == comboBox1.InnerTextBox) return "COM-порт";
        return tb.Name ?? "Текстовое поле";
    }

    private void ToggleNumpadButton_Click(object? sender, RoutedEventArgs e)
    {
        if (numpadWindow == null)
        {
            InitializeNumpad();
        }

        if (numpadWindow!.IsVisible)
        {
            numpadWindow.Hide();
        }
        else
        {
            numpadWindow.Show(this);
            // Позиционируем справа от главного окна
            numpadWindow.Position = new PixelPoint(
                (int)(this.Position.X + this.Bounds.Width - 250),
                (int)(this.Position.Y + 120));
            if (activeTextBox != null)
            {
                numpadWindow.SetTargetField(GetTextBoxFriendlyName(activeTextBox));
            }
        }
    }

    // =========================================================================
    // ОБРАБОТКА КЛАВИАТУРНЫХ СТРЕЛОК ДЛЯ ДЖОГА
    // =========================================================================
    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        // Не перехватываем стрелки, если фокус находится в текстовом поле
        if (e.Source is TextBox) return;

        switch (e.Key)
        {
            case Key.Up:
                buttonUpXY_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.Down:
                buttonDownXY_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.Left:
                buttonLeftXY_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.Right:
                buttonRightXY_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
        }
    }

    // =========================================================================
    // СИСТЕМНЫЕ КНОПКИ: О ПРОГРАММЕ И ОБНОВЛЕНИЕ
    // =========================================================================
    private void button9_Click_2(object? sender, RoutedEventArgs e)
    {
        var aboutWin = new AboutWindow();
        aboutWin.ShowDialog(this);
    }

    private void button8_ClickAsync(object? sender, RoutedEventArgs e)
    {
        var updWin = new UpdateWindow();
        updWin.ShowDialog(this);
    }

    private void SliderOpacity_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (groupBox3 != null)
        {
            this.Opacity = e.NewValue;
        }
    }

    // =========================================================================
    // ЗАКРЫТИЕ ПРИЛОЖЕНИЯ И ОСВОБОЖДЕНИЕ РЕСУРСОВ
    // =========================================================================
    // =========================================================================
    // АДАПТАЦИЯ НАЧАЛЬНОГО РАЗМЕРА ОКНА ПОД РАЗРЕШЕНИЕ ЭКРАНА
    // Если рабочая область экрана меньше 1440x860 (с учетом масштабирования Windows),
    // окно автоматически пропорционально уменьшается (до 85-90% рабочей области),
    // чтобы оставались видимые рамки и не возникало проблем с масштабированием.
    // =========================================================================
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        AdjustInitialWindowSize();
    }

    private void AdjustInitialWindowSize()
    {
        try
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen == null) return;

            // Коэффициент масштабирования экрана (DPI scaling, например 1.0, 1.25, 1.5)
            double scaling = screen.Scaling;
            if (scaling <= 0) scaling = 1.0;

            // Реальная доступная рабочая область экрана в DIPs (без панели задач)
            double workAreaW = screen.WorkingArea.Width / scaling;
            double workAreaH = screen.WorkingArea.Height / scaling;

            double targetW = 1440;
            double targetH = 860;

            // Задаем безопасный максимальный размер (88% от рабочей области экрана),
            // гарантирующий наличие удобных рамок для перетаскивания и изменения размера
            double maxAllowedW = workAreaW * 0.88;
            double maxAllowedH = workAreaH * 0.88;

            // Если базовый размер не умещается в безопасную зону рабочей области
            if (targetW > maxAllowedW || targetH > maxAllowedH)
            {
                double scaleFactor = Math.Min(maxAllowedW / targetW, maxAllowedH / targetH);
                targetW = Math.Max(MinWidth, Math.Round(targetW * scaleFactor));
                targetH = Math.Max(MinHeight, Math.Round(targetH * scaleFactor));

                this.Width = targetW;
                this.Height = targetH;

                // Центрируем окно в видимой рабочей области
                double leftDips = (screen.WorkingArea.X / scaling) + Math.Max(0, (workAreaW - targetW) / 2.0);
                double topDips = (screen.WorkingArea.Y / scaling) + Math.Max(0, (workAreaH - targetH) / 2.0);

                this.Position = new PixelPoint(
                    (int)Math.Round(leftDips * scaling),
                    (int)Math.Round(topDips * scaling)
                );
            }
        }
        catch { }
    }

    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        try
        {
            timer1?.Stop();
            if (numpadWindow != null)
            {
                numpadWindow.AllowClose = true;
                numpadWindow.Close();
            }
            port?.Close();
        }
        catch { }
    }
}
