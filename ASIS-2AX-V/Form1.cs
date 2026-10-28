using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime;
using System.Threading.Tasks;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace G_write
{
    public partial class Form1 : Form
    {
        string stepHandZ = "";
        float timeWater = 1;
        float start_w;
        private bool isCounterCommand = false;
        double x, y, z;
        string sx = "0.0", sy = "0.0", sz = "0.0";

        // СТАЛО: Кэш предыдущих значений координат (устраняет лишние перерисовки меток на слабом железе)
        private string lastSx = "", lastSy = "", lastSz = "";

        int i = 0;
        int j = 0;
        float length = 0;
        private TaskCompletionSource<string>? responseTcs;
        public string mydocu = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        public string receiveOk;
        public string boxtext;
        public bool timeIsReady = false;
        public int DeviceConnectAttempts = 0;
        float xStart = 0, yStart = 0, xEnd = 0, yEnd = 0, step = 0;
        bool isConnected = false;
        String[] ports;
        String[] steps = ["10", "20", "50", "60"];
        String[] speeds = ["50", "100", "200", "400"];
        String stepHand = "10";
        String speedHand = (Int32.Parse("10") * 60).ToString();
        String speedWater;

        // СТАЛО: Использование интерфейса ICNCConnection для поддержки как Serial COM, так и Wi-Fi TCP
        // БЫЛО: SerialPort port;
        private ICNCConnection? port;
        private ConnectionConfig connectionConfig = ConnectionConfig.Load();

        public char logReceive;
        public string receiveData;
        public string str;
        public string currentX;
        public string currentY;
        public string currentZ;
        public string currentX_str;
        public string currentY_str;
        public bool show = false;
        public bool sendDone = false;
        public bool isNotPaused = true;
        public bool isNotStop = true;
        public bool isNotStopCounter = true;

        // СТАЛО: Флаг активной потоковой передачи G-кода для безопасного управления паузой и отменой
        public bool isStreaming = false;

        // СТАЛО: Порог ограничения строк лога (кольцевой буфер, исключающий переполнение памяти и лаги интерфейса)
        private const int MaxTerminalLines = 300;

        public bool ok = true;
        string[] dataToSend;
        private readonly object tcsLock = new object();
        private NumpadForm numpadForm;
        private Control activeControl;
        string tempPath = Path.Combine(Application.StartupPath, "temp.txt");

        public Form1()
        {
            InitializeComponent();
            this.SetStyle(ControlStyles.AllPaintingInWmPaint |
                  ControlStyles.UserPaint |
                  ControlStyles.DoubleBuffer, true);
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

            // Приоритет процесса
            System.Diagnostics.Process.GetCurrentProcess().PriorityClass =
                System.Diagnostics.ProcessPriorityClass.High;
            this.UpdateStyles();
            this.StartPosition = FormStartPosition.CenterScreen;
            this.KeyPreview = true;
            InitializeNumpad();
            SubscribeToTextBoxEvents(this);
            button11.ImageAlign = ContentAlignment.MiddleLeft;
            label17.UseMnemonic = false;
            label18.UseMnemonic = false;
            label19.UseMnemonic = false;

            InitConnectionSettings();
        }

        // =========================================================================
        // СТАЛО: Оптимизированный вывод в терминал (устраняет зависания на слабом ПК)
        // БЫЛО: В коде использовалось box.Text += ...
        //       Это считывало весь текст из Win32 RichEdit, пересоздавало строку в памяти
        //       и заново перерисовывало документ с 1-й строки, вызывая фризы на секунды.
        // =========================================================================
        private void AppendLog(string text)
        {
            if (this.IsDisposed || !this.IsHandleCreated) return;

            if (this.InvokeRequired)
            {
                this.BeginInvoke(new MethodInvoker(() => AppendLog(text)));
                return;
            }

            if (box.Lines.Length > MaxTerminalLines)
            {
                int cutIndex = box.GetFirstCharIndexFromLine(80);
                if (cutIndex > 0)
                {
                    box.Select(0, cutIndex);
                    box.SelectedText = string.Empty;
                }
            }

            box.AppendText(text + "\r\n");
            box.ScrollToCaret();
        }

        // =========================================================================
        // СТАЛО: Настройка интерфейса соединения вынесена в Form1.Designer.cs / Form1.resx (WinForms Designer)
        //       В коде только привязка конфигурации и логика переключения видимости.
        // =========================================================================
        private void InitConnectionSettings()
        {
            txtTcpIp.Text = connectionConfig.TcpHost;
            txtTcpPort.Text = connectionConfig.TcpPort.ToString();
            if (connectionConfig.IsTcpMode)
            {
                rbTcp.Checked = true;
            }
            else
            {
                rbSerial.Checked = true;
            }

            RefreshComPorts();
            SwitchConnectionMode(connectionConfig.IsTcpMode);
        }

        private void rbSerial_CheckedChanged(object? sender, EventArgs e)
        {
            if (rbSerial.Checked)
            {
                SwitchConnectionMode(false);
            }
        }

        private void rbTcp_CheckedChanged(object? sender, EventArgs e)
        {
            if (rbTcp.Checked)
            {
                SwitchConnectionMode(true);
            }
        }

        private void SwitchConnectionMode(bool isTcp)
        {
            connectionConfig.IsTcpMode = isTcp;
            connectionConfig.Save();

            button2.Visible = !isTcp;
            comboBox1.Visible = !isTcp;

            lblTcpIp.Visible = isTcp;
            txtTcpIp.Visible = isTcp;
            lblTcpPort.Visible = isTcp;
            txtTcpPort.Visible = isTcp;
        }

        private void RefreshComPorts()
        {
            try
            {
                ports = SerialPort.GetPortNames();
                comboBox1.Items.Clear();
                foreach (string p in ports)
                {
                    comboBox1.Items.Add(p);
                }
                if (!string.IsNullOrEmpty(connectionConfig.LastComPort) && comboBox1.Items.Contains(connectionConfig.LastComPort))
                {
                    comboBox1.SelectedItem = connectionConfig.LastComPort;
                }
                else if (comboBox1.Items.Count > 0)
                {
                    comboBox1.SelectedIndex = 0;
                }
            }
            catch { }
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            try { File.Delete(tempPath); } catch { return; }
        }

        private void InitializeNumpad()
        {
            numpadForm = new NumpadForm();

            numpadForm.NumberPressed += (s, num) =>
            {
                if (activeControl is System.Windows.Forms.TextBox textBox)
                    textBox.Text += num;
                else if (activeControl is System.Windows.Forms.ComboBox comboBox)
                    comboBox.Text += num;
            };

            numpadForm.BackspacePressed += (s, e) =>
            {
                if (activeControl is System.Windows.Forms.TextBox textBox && textBox.Text.Length > 0)
                    textBox.Text = textBox.Text[..^1];
                if (activeControl is System.Windows.Forms.ComboBox comboBox && comboBox.Text.Length > 0)
                    comboBox.Text = comboBox.Text[..^1];
            };

            numpadForm.EnterPressed += (s, e) =>
            {
                SelectNextControl(activeControl, true, true, true, true);
            };

            numpadForm.ClearPressed += (s, e) =>
            {
                if (activeControl is System.Windows.Forms.TextBox textBox)
                    textBox.Clear();
                if (activeControl is System.Windows.Forms.ComboBox comboBox)
                    comboBox.SelectedIndex = 0;
            };

            numpadForm.FormClosing += (s, e) =>
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    numpadForm.Hide();
                }
            };
        }

        private void SubscribeToTextBoxEvents(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                if (control is System.Windows.Forms.TextBox)
                {
                    control.GotFocus += (s, e) => activeControl = control;
                }
                if (control is System.Windows.Forms.ComboBox)
                {
                    control.GotFocus += (s, e) => activeControl = control;
                }

                if (control.HasChildren)
                {
                    SubscribeToTextBoxEvents(control);
                }
            }
        }

        private void ToggleNumpadButton_Click(object sender, EventArgs e)
        {
            if (numpadForm.Visible)
            {
                numpadForm.Hide();
                ToggleNumpadButton.Text = "Экранная\nклавиатура";
            }
            else
            {
                numpadForm.Location = new Point(this.Right + 10, this.Top);
                numpadForm.Show();
                ToggleNumpadButton.Text = "Закрыть\nклавиатуру";
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            RefreshComPorts();
            box.Font = new Font("Consolas", 10f, FontStyle.Regular);
            Control.Enabled = false;
            Zigzag.Enabled = true;
            comboBox2.Items.Clear();
            comboBox3.Items.Clear();
            button10.Enabled = false;
            button12.Enabled = false;
            button3.Enabled = false;
            button11.Enabled = false;

            foreach (string step in steps)
            {
                comboBox2.Items.Add(step);
            }
            foreach (string speed in speeds)
            {
                comboBox3.Items.Add(speed);
                comboBox4.Items.Add(speed);
            }
            comboBox3.SelectedItem = "10";
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            this.Focus();
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Left || keyData == Keys.Right ||
                keyData == Keys.Up || keyData == Keys.Down)
            {
                HandleArrowKey(keyData);
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }

        private void HandleArrowKey(Keys key)
        {
            string message = key.ToString().Replace("Keys.", "");
            if (message == "Left")
            {
                buttonLeftXY.PerformClick();
            }
            if (message == "Right")
            {
                buttonRightXY.PerformClick();
            }
            if (message == "Up")
            {
                buttonUpXY.PerformClick();
            }
            if (message == "Down")
            {
                buttonDownXY.PerformClick();
            }
        }

        private void genButton_Click(object sender, EventArgs e)
        {
            gen();
            Control.Focus();
        }

        private float ParseFloatSafe(string input)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(input))
                {
                    Task.Run(async () =>
                    {
                        await BlinkButtonAsync(genButton, TimeSpan.FromMilliseconds(250), 3, Color.Crimson);
                    });
                    MessageBox.Show("Введите значение", "Предупреждение",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return 0.987612345f;
                }

                input = input.Trim().Replace(',', '.');

                if (!System.Text.RegularExpressions.Regex.IsMatch(input, @"^-?[0-9]*\.?[0-9]+$"))
                {
                    Task.Run(async () =>
                    {
                        await BlinkButtonAsync(genButton, TimeSpan.FromMilliseconds(250), 3, Color.Crimson);
                    });
                    MessageBox.Show("Введите корректное число (только цифры, точка и знак минус)",
                        "Ошибка формата", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return 0.987612345f;
                }

                if (input.Count(c => c == '.') > 1)
                {
                    Task.Run(async () =>
                    {
                        await BlinkButtonAsync(genButton, TimeSpan.FromMilliseconds(250), 3, Color.Crimson);
                    });
                    MessageBox.Show("Число содержит более одного десятичного разделителя",
                        "Ошибка формата", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return 0.987612345f;
                }

                if (input.Contains("-") && !input.StartsWith("-"))
                {
                    Task.Run(async () =>
                    {
                        await BlinkButtonAsync(genButton, TimeSpan.FromMilliseconds(250), 3, Color.Crimson);
                    });
                    MessageBox.Show("Знак минус может быть только в начале числа",
                        "Ошибка формата", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return 0.987612345f;
                }

                string numberPart = input.TrimStart('-');
                if (string.IsNullOrEmpty(numberPart) || numberPart == ".")
                {
                    Task.Run(async () =>
                    {
                        await BlinkButtonAsync(genButton, TimeSpan.FromMilliseconds(250), 3, Color.Crimson);
                    });
                    MessageBox.Show("Введите число после знака минус",
                        "Ошибка формата", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return 0.987612345f;
                }

                if (float.TryParse(input,
                    NumberStyles.Float | NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out float result))
                {
                    if (result > float.MaxValue || result < float.MinValue)
                    {
                        Task.Run(async () =>
                        {
                            await BlinkButtonAsync(genButton, TimeSpan.FromMilliseconds(250), 3, Color.Crimson);
                        });
                        MessageBox.Show($"Значение выходит за пределы допустимого диапазона (от {float.MinValue} до {float.MaxValue})",
                            "Ошибка диапазона", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return 0.987612345f;
                    }

                    return result;
                }
                else
                {
                    Task.Run(async () =>
                    {
                        await BlinkButtonAsync(genButton, TimeSpan.FromMilliseconds(250), 3, Color.Crimson);
                    });
                    MessageBox.Show("Не удалось преобразовать введенное значение в число",
                        "Ошибка преобразования", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return 0.987612345f;
                }
            }
            catch (Exception ex)
            {
                Task.Run(async () =>
                {
                    await BlinkButtonAsync(genButton, TimeSpan.FromMilliseconds(250), 3, Color.Crimson);
                });
                MessageBox.Show($"Ошибка при обработке данных: {ex.Message}",
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 0.987612345f;
            }
        }

        void SaveCoordinates(string file, double[] coords)
        {
            string header = $"; x1={coords[0]}\n; y1={coords[1]}\n; x2={coords[2]}\n; y2={coords[3]}\n; wtm={coords[4]}\n; spd={coords[5]}\n; stp={coords[6]}\n\n";
            File.WriteAllText(file, header + (File.Exists(file) ? File.ReadAllText(file) : ""));
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
                    double.TryParse(line.Split('=')[1].Trim(), out coords[idx++]);
                }
            }
            return coords;
        }

        public void gen()
        {
            if ((ParseFloatSafe(startPosX.Text) == 0.987612345f)
            || ParseFloatSafe(startPosY.Text) == 0.987612345f
            || ParseFloatSafe(endPosX.Text) == 0.987612345f
            || ParseFloatSafe(endPosY.Text) == 0.987612345f
            || ParseFloatSafe(comboBox2.Text) == 0.987612345f
            || ParseFloatSafe(comboBox4.Text) == 0.987612345f
            || ParseFloatSafe(comboBox6.Text) == 0.987612345f)
            { return; }
            else
            {
                xStart = ParseFloatSafe(startPosX.Text);
                yStart = ParseFloatSafe(startPosY.Text);
                xEnd = ParseFloatSafe(endPosX.Text);
                yEnd = ParseFloatSafe(endPosY.Text);
                step = ParseFloatSafe(comboBox2.Text);
                start_w = ParseFloatSafe(comboBox6.Text);
            }

            // БЫЛО: StreamWriter outline = new StreamWriter(tempPath); ... outline.Close();
            // СТАЛО: Использование блока using для гарантированного освобождения файла
            using (StreamWriter outline = new StreamWriter(tempPath))
            {
                float Xl = xEnd - xStart;
                float Yl = yEnd - yStart;
                int nY = (int)Math.Truncate(Yl / step);
                float tY = (Yl / step - nY) * step;
                int pY = 1;
                outline.WriteLine("G90");
                outline.WriteLine("G17");
                outline.WriteLine("G10 L20 P1 Z0");
                outline.WriteLine("G01 Z " + start_w.ToString() + " F " + (ParseFloatSafe(comboBox4.Text) * 60).ToString());

                try
                {
                    timeWater = start_w;
                }
                catch
                {
                    Task.Run(async () =>
                    {
                        await BlinkButtonAsync(genButton, TimeSpan.FromMilliseconds(250), 3, Color.Crimson);
                    });
                    MessageBox.Show("Заново укажите время прокачки");
                }
                timeWater++;
                outline.WriteLine("G01" + " X " + xStart.ToString().Replace(",", ".") + " Y " + yStart.ToString().Replace(",", ".") + $" Z {timeWater.ToString()} " + "F" + (ParseFloatSafe(comboBox4.Text) * 60).ToString());
                timeWater++;
                outline.WriteLine("G01" + " X " + xEnd.ToString().Replace(",", ".") + $" Z {timeWater.ToString()} " + "F" + (ParseFloatSafe(comboBox4.Text) * 60).ToString());
                timeWater++;
                length += (xEnd - xStart);
                if (yEnd > step)
                {
                    while (!(pY > nY))
                    {
                        if (!((pY % 2) == 0))
                        {
                            outline.WriteLine("G01 " + " Y " + pY * step + $" Z {timeWater.ToString()} " + "F" + (ParseFloatSafe(comboBox4.Text) * 60).ToString());
                            timeWater++;
                            outline.WriteLine("G01 " + " X " + xStart.ToString().Replace(",", ".") + $" Z {timeWater.ToString()} " + "F" + (ParseFloatSafe(comboBox4.Text) * 60).ToString());
                            timeWater++;
                            length += (step + (xEnd - xStart));
                            pY++;
                        }
                        else
                        {
                            outline.WriteLine("G01 " + " Y " + pY * step + $" Z {timeWater.ToString()} " + "F" + (ParseFloatSafe(comboBox4.Text) * 60).ToString());
                            timeWater++;
                            outline.WriteLine("G01 " + " X " + xEnd.ToString().Replace(",", ".") + $" Z {timeWater.ToString()} " + "F" + (ParseFloatSafe(comboBox4.Text) * 60).ToString());
                            timeWater++;
                            length += (step + (xEnd - xStart));
                            pY++;
                        }
                    }

                    if (((yEnd % step) != 0))
                    {
                        if (!((pY % 2) == 0))
                        {
                            outline.WriteLine("G01 " + " Y " + (tY + (pY - 1) * step) + $" Z {timeWater.ToString()} " + "F" + (ParseFloatSafe(comboBox4.Text) * 60).ToString());
                            timeWater++;
                            outline.WriteLine("G01 " + " X " + xStart.ToString().Replace(",", ".") + $" Z {timeWater.ToString()} " + "F" + (ParseFloatSafe(comboBox4.Text) * 60).ToString());
                            timeWater++;
                            length += (tY + (xEnd - xStart));
                        }
                        else
                        {
                            outline.WriteLine("G01 " + " Y " + (tY + (pY - 1) * step) + $" Z {timeWater.ToString()} " + "F" + (ParseFloatSafe(comboBox4.Text) * 60).ToString());
                            timeWater++;
                            outline.WriteLine("G01 " + " X " + xEnd.ToString().Replace(",", ".") + $" Z {timeWater.ToString()} " + "F" + (ParseFloatSafe(comboBox4.Text) * 60).ToString());
                            timeWater++;
                            length += (tY + (xEnd - xStart));
                        }
                    }
                }
            }

            SaveCoordinates(tempPath, new double[] { xStart, yStart, xEnd, yEnd, start_w, ParseFloatSafe(comboBox4.Text), step });
            label16.Text = (length / Int32.Parse(comboBox4.Text)).ToString();
            length = 0;
            Task.Run(async () =>
            {
                await BlinkButtonAsync(genButton, TimeSpan.FromMilliseconds(250), 3, Color.Green);
            });
        }

        private async Task BlinkButtonAsync(Button button,
        TimeSpan interval,
        int blinkCount,
        Color firstColor)
        {
            Color secondColor = Color.White;
            Color originalColor = button.BackColor;

            for (int k = 0; k < blinkCount; k++)
            {
                button.BackColor = firstColor;
                await Task.Delay(interval);

                button.BackColor = secondColor;
                await Task.Delay(interval);
            }
            button.BackColor = originalColor;
        }

        // =========================================================================
        // СТАЛО: Унифицированное подключение через COM или TCP Wi-Fi
        // БЫЛО: Прямой экземпляр SerialPort port = new SerialPort(...)
        // =========================================================================
        private void connect()
        {
            if (rbTcp.Checked)
            {
                string host = txtTcpIp.Text.Trim();
                if (string.IsNullOrEmpty(host))
                {
                    MessageBox.Show("Укажите IP-адрес для подключения по Wi-Fi", "Предупреждение", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!int.TryParse(txtTcpPort.Text.Trim(), out int tcpPort) || tcpPort <= 0 || tcpPort > 65535)
                {
                    MessageBox.Show("Укажите корректный TCP-порт (1-65535)", "Предупреждение", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                connectionConfig.TcpHost = host;
                connectionConfig.TcpPort = tcpPort;
                connectionConfig.IsTcpMode = true;
                connectionConfig.Save();

                lblConnStatus.Text = "Подключение...";
                lblConnStatus.ForeColor = Color.OrangeRed;

                try
                {
                    if (port != null)
                    {
                        port.Close();
                        port.Dispose();
                    }

                    var tcpConn = new TcpCNCConnection(host, tcpPort);
                    tcpConn.LineReceived += (s, line) => HandleIncomingLine(line);
                    tcpConn.Disconnected += (s, e) =>
                    {
                        this.BeginInvoke(new MethodInvoker(() =>
                        {
                            if (isConnected) disconnect();
                        }));
                    };

                    tcpConn.Open();
                    port = tcpConn;

                    lblConnStatus.Text = $"TCP: {host}:{tcpPort}";
                    lblConnStatus.ForeColor = Color.DarkGreen;
                }
                catch (Exception ex)
                {
                    lblConnStatus.Text = "Ошибка связи";
                    lblConnStatus.ForeColor = Color.Red;
                    MessageBox.Show($"Ошибка подключения по TCP: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            else
            {
                string selectedPort = comboBox1.GetItemText(comboBox1.SelectedItem) ?? "";
                if (string.IsNullOrEmpty(selectedPort))
                {
                    MessageBox.Show("Выберите нужный COM-порт", "Предупреждение", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                connectionConfig.LastComPort = selectedPort;
                connectionConfig.IsTcpMode = false;
                connectionConfig.Save();

                lblConnStatus.Text = "Подключение...";
                lblConnStatus.ForeColor = Color.OrangeRed;

                try
                {
                    if (port != null)
                    {
                        port.Close();
                        port.Dispose();
                    }

                    var serialConn = new SerialCNCConnection(selectedPort, 115200);
                    serialConn.LineReceived += (s, line) => HandleIncomingLine(line);
                    serialConn.Disconnected += (s, e) =>
                    {
                        this.BeginInvoke(new MethodInvoker(() =>
                        {
                            if (isConnected) disconnect();
                        }));
                    };

                    serialConn.Open();
                    port = serialConn;

                    lblConnStatus.Text = $"COM: {selectedPort}";
                    lblConnStatus.ForeColor = Color.DarkGreen;
                }
                catch (Exception ex)
                {
                    lblConnStatus.Text = "Ошибка порта";
                    lblConnStatus.ForeColor = Color.Red;
                    MessageBox.Show($"Ошибка подключения к {selectedPort}: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            try
            {
                InitTimer();
                button1.Text = "Отключить";
                Control.Enabled = true;
                Zigzag.Enabled = true;
                isConnected = true;
                button10.Enabled = true;
                button12.Enabled = true;
                button3.Enabled = true;
                button11.Enabled = true;

                rbSerial.Enabled = false;
                rbTcp.Enabled = false;
                txtTcpIp.Enabled = false;
                txtTcpPort.Enabled = false;

                Control.Focus();
            }
            catch
            {
                MessageBox.Show("Ошибка инициализации интерфейса");
            }
        }

        private void disconnect()
        {
            isConnected = false;
            isStreaming = false;

            try
            {
                if (timer1 != null)
                {
                    timer1.Stop();
                }
                if (port != null)
                {
                    port.Close();
                    port.Dispose();
                }
            }
            catch { }

            button1.Text = "Подключить";
            Control.Enabled = false;
            button10.Enabled = false;
            Zigzag.Enabled = false;
            button12.Enabled = false;
            button3.Enabled = false;
            button11.Enabled = false;

            rbSerial.Enabled = true;
            rbTcp.Enabled = true;
            txtTcpIp.Enabled = true;
            txtTcpPort.Enabled = true;

            lblConnStatus.Text = "Отключено";
            lblConnStatus.ForeColor = Color.DimGray;
        }

        // =========================================================================
        // СТАЛО: Оптимизированный разбор WPos без тяжелого Regex + кэш координат
        // БЫЛО: Regex.Match(line, @"WPos:([-0-9.]+),([-0-9.]+),([-0-9.]+)") на каждой строке
        // =========================================================================
        private void HandleIncomingLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return;

            line = line.Trim();

            if (line.StartsWith("<") && line.Contains("WPos:"))
            {
                int wposIdx = line.IndexOf("WPos:");
                if (wposIdx >= 0)
                {
                    int start = wposIdx + 5;
                    int comma1 = line.IndexOf(',', start);
                    if (comma1 > start)
                    {
                        int comma2 = line.IndexOf(',', comma1 + 1);
                        if (comma2 > comma1)
                        {
                            int end = line.IndexOfAny(new char[] { '|', '>' }, comma2 + 1);
                            if (end > comma2)
                            {
                                string partX = line.Substring(start, comma1 - start);
                                string partY = line.Substring(comma1 + 1, comma2 - comma1 - 1);
                                string partZ = line.Substring(comma2 + 1, end - comma2 - 1);

                                if (double.TryParse(partX, NumberStyles.Float, CultureInfo.InvariantCulture, out double dx) &&
                                    double.TryParse(partY, NumberStyles.Float, CultureInfo.InvariantCulture, out double dy) &&
                                    double.TryParse(partZ, NumberStyles.Float, CultureInfo.InvariantCulture, out double dz))
                                {
                                    string newSx = Math.Round(dx, 1).ToString("F1", CultureInfo.InvariantCulture);
                                    string newSy = Math.Round(dy, 1).ToString("F1", CultureInfo.InvariantCulture);
                                    string newSz = Math.Round(dz, 1).ToString("F1", CultureInfo.InvariantCulture);

                                    sx = newSx;
                                    sy = newSy;
                                    sz = newSz;

                                    if (newSx != lastSx || newSy != lastSy || newSz != lastSz)
                                    {
                                        lastSx = newSx;
                                        lastSy = newSy;
                                        lastSz = newSz;

                                        if (!this.IsDisposed && this.IsHandleCreated)
                                        {
                                            this.BeginInvoke(new MethodInvoker(() =>
                                            {
                                                label17.Text = $"X: {newSx}";
                                                label18.Text = $"Y: {newSy}";
                                                label19.Text = $"Z: {newSz}";
                                            }));
                                        }
                                    }
                                }
                                return;
                            }
                        }
                    }
                }
            }

            if (isCounterCommand && line == "ok")
            {
                isCounterCommand = false;
                return;
            }

            AppendLog(line);

            lock (tcsLock)
            {
                if (responseTcs != null && !responseTcs.Task.IsCompleted)
                {
                    if (line.StartsWith("ok") || line.StartsWith("error:"))
                    {
                        responseTcs.TrySetResult(line);
                    }
                }
            }
        }

        private void button1_Click(object sender, EventArgs e)
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

        private void button2_Click(object sender, EventArgs e)
        {
            if (!isConnected)
            {
                RefreshComPorts();
            }
        }

        // =========================================================================
        // БЫЛО: Рекурсивный метод SendDataAndWaitForResponse
        // /*
        // private async Task SendDataAndWaitForResponse(string data)
        // {
        //     if (port.IsOpen)
        //     {
        //         if (data != "done")
        //         {
        //             lock (tcsLock) { responseTcs = new TaskCompletionSource<string>(); }
        //             port.WriteLine(data + "\n");
        //             box.Text += data + "\n";
        //             string response = await responseTcs.Task;
        //             box.Text += response + "\n";
        //             string newDataToSend = GenerateNewDataBasedOnResponse(response);
        //             await SendDataAndWaitForResponse(newDataToSend);
        //         }
        //     }
        // }
        // */
        // СТАЛО: Плоский асинхронный цикл с таймаутом подтверждения 5 секунд.
        //       Исключает StackOverflowException на больших управляющих программах,
        //       корректно обрабатывает паузу, возобновление и аварийную остановку.
        // =========================================================================
        private async Task StreamGCodeAsync()
        {
            if (port == null || !port.IsOpen || dataToSend == null || dataToSend.Length == 0) return;

            isStreaming = true;
            sendDone = false;

            progressBar1.Minimum = 0;
            progressBar1.Maximum = dataToSend.Length;

            while (i < dataToSend.Length && isNotStop && isStreaming)
            {
                while (!isNotPaused && isNotStop && isStreaming)
                {
                    await Task.Delay(100);
                }

                if (!isNotStop || !isStreaming) break;

                string command = dataToSend[i];
                if (string.IsNullOrWhiteSpace(command) || command.StartsWith(";"))
                {
                    i++;
                    continue;
                }

                TaskCompletionSource<string> tcs;
                lock (tcsLock)
                {
                    responseTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                    tcs = responseTcs;
                }

                try
                {
                    isCounterCommand = false;
                    AppendLog(command);
                    port.WriteLine(command);

                    Task completedTask = await Task.WhenAny(tcs.Task, Task.Delay(5000));
                    if (completedTask != tcs.Task)
                    {
                        AppendLog("! Предупреждение: таймаут ожидания ответа 'ok' (5 сек)");
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"! Ошибка передачи команды: {ex.Message}");
                    break;
                }

                i++;
                progressBar1.Value = Math.Min(i, progressBar1.Maximum);
                label10.Text = "Отправка" + new string('.', (j % 4));
                j++;
            }

            if (i >= dataToSend.Length)
            {
                progressBar1.Value = progressBar1.Maximum;
                label10.Text = "Завершено";
                sendDone = true;
                i = 0;
            }

            isStreaming = false;
        }

        private async void button3_Click(object sender, EventArgs e)
        {
            if (!File.Exists(tempPath))
            {
                MessageBox.Show("Файл траектории не найден. Сначала сгенерируйте код.");
                return;
            }

            dataToSend = File.ReadAllLines(tempPath);
            if (dataToSend.Length == 0) return;

            if (!isStreaming)
            {
                i = 0;
                await StreamGCodeAsync();
            }
        }

        private async void Button4_Click(object sender, EventArgs e)
        {
            if (isConnected == true)
            {
                try
                {
                    if (customBox.Text.Length >= 2 && customBox.Text[0] == '$' && customBox.Text[1] == '$')
                    {
                        // БЫЛО: timer1.Dispose();
                        // СТАЛО: timer1.Stop() предотвращает ObjectDisposedException
                        timer1?.Stop();
                        MessageBox.Show("Во избежание ошибок во время настройки контроллера счетчик был отключен.\nПосле завершения кликните по счётчику для возобновления.");
                    }

                    port?.WriteLine(customBox.Text);
                }
                catch { return; }

                AppendLog(customBox.Text);
            }
            else
            {
                MessageBox.Show("Порт закрыт");
            }
        }

        private void button5_Click(object sender, EventArgs e)
        {
            box.Clear();
        }

        private void radioButton1_CheckedChanged(object sender, EventArgs e)
        {
            stepHand = "0.1";
        }

        private void radioButton2_CheckedChanged(object sender, EventArgs e)
        {
            stepHand = "1";
        }

        private void radioButton3_CheckedChanged(object sender, EventArgs e)
        {
            stepHand = "10";
        }

        private void radioButton4_CheckedChanged(object sender, EventArgs e)
        {
            stepHand = "5";
        }

        private void radioButton5_CheckedChanged(object sender, EventArgs e)
        {
            stepHand = "50";
        }

        private void radioButton6_CheckedChanged(object sender, EventArgs e)
        {
            stepHand = "100";
        }

        private void buttonUpXY_Click(object sender, EventArgs e)
        {
            isCounterCommand = false;
            try
            {
                if ((radioButton1.Checked || radioButton2.Checked || radioButton3.Checked || radioButton4.Checked || radioButton5.Checked || radioButton6.Checked) && comboBox3.Text.Length > 0)
                {
                    speedHand = (Int32.Parse(comboBox3.Text) * 60).ToString();
                    port?.Write("G91\n\f");
                    port?.Write("G01 X " + "" + stepHand + " F" + speedHand + "\n");
                }
                else
                {
                    MessageBox.Show("Не все значения выбраны");
                }
            }
            catch { return; }

            AppendLog("G01 Y" + "" + stepHand + " F" + speedHand);
        }

        private void buttonDownXY_Click(object sender, EventArgs e)
        {
            WriteIt("G01 X -" + stepHand + " F" + speedHand, "G91");
            isCounterCommand = false;
        }

        private void buttonLeftXY_Click(object sender, EventArgs e)
        {
            WriteIt("G01 Y " + "-" + stepHand + "F" + speedHand, "G91");
            isCounterCommand = false;
        }

        private void buttonRightXY_Click(object sender, EventArgs e)
        {
            WriteIt("G01 Y" + "" + stepHand + "F" + speedHand, "G91");
            isCounterCommand = false;
        }

        private void buttonUpZ_Click(object sender, EventArgs e)
        {
            speedWater = comboBox7.Text;
            stepHandZ = comboBox5.Text;
            if (speedWater != "" && stepHandZ != "")
            {
                WriteIt("G01 Z" + "" + stepHandZ + " F" + speedWater, "G91");
            }
            else
            {
                MessageBox.Show("Введите значение", "Предупреждение",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            isCounterCommand = false;
        }

        private void buttonDownZ_Click(object sender, EventArgs e)
        {
            speedWater = comboBox7.Text;
            stepHandZ = comboBox5.Text;
            if (speedWater != "" && stepHandZ != "")
            {
                WriteIt("G01 Z" + "-" + stepHandZ + " F" + speedWater, "G91");
            }
            else
            {
                MessageBox.Show("Введите значение", "Предупреждение",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            isCounterCommand = false;
        }

        private void buttonHomeXY_Click(object sender, EventArgs e)
        {
            try
            {
                port?.Write("G28 X0 Y0\n");
                isCounterCommand = false;
                AppendLog("G28 X0 Y0");
            }
            catch { return; }
        }

        public void WriteIt(string toWrite, string prefix)
        {
            try
            {
                if ((radioButton1.Checked || radioButton2.Checked || radioButton3.Checked || radioButton4.Checked || radioButton5.Checked || radioButton6.Checked) && comboBox3.Text.Length > 0)
                {
                    speedHand = (Int32.Parse(comboBox3.Text) * 60).ToString();
                    port?.Write(prefix + "\n");
                    isCounterCommand = false;
                    port?.Write(toWrite + "\n");
                    isCounterCommand = false;
                    AppendLog(toWrite);
                }
                else
                {
                    MessageBox.Show("Не все значения выбраны");
                }
            }
            catch { return; }
        }

        private void label9_Click(object sender, EventArgs e)
        {
        }

        private Timer timer1;

        public void InitTimer()
        {
            timer1 = new Timer();
            timer1.Tick += new EventHandler(timer1_Tick);
            timer1.Interval = 100;
            timer1.Start();
        }

        private void timer1_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (port != null && port.IsOpen)
                {
                    isCounterCommand = true;
                    port.WriteLine("?");
                }
            }
            catch { }
        }

        private void comboBox3_SelectedIndexChanged(object sender, EventArgs e)
        {
            speedHand = (Int32.Parse(comboBox3.Text) * 60).ToString();
        }

        private void comboBox3_TextChanged(object sender, EventArgs e)
        {
            speedHand = (Int32.Parse(comboBox3.Text) * 60).ToString();
        }

        private void comboBox3_MouseLeave(object sender, EventArgs e)
        {
            speedHand = (Int32.Parse(comboBox3.Text) * 60).ToString();
        }

        private void customBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                button4.PerformClick();
            }
        }

        private void button6_Click(object sender, EventArgs e)
        {
            startPosX.Text = sx;
            startPosY.Text = sy;
        }

        private void button7_Click(object sender, EventArgs e)
        {
            endPosX.Text = sx;
            endPosY.Text = sy;
        }

        private void button8_ClickAsync(object sender, EventArgs e)
        {
            update update = new update();
            update.Show();
        }

        private void button10_Click(object sender, EventArgs e)
        {
            byte[] data = new byte[] { 0x82 };
            port?.Write(data, 0, data.Length);
        }

        private void button11_Click(object sender, EventArgs e)
        {
            if (isNotPaused)
            {
                isNotPaused = false;
                // БЫЛО: timer1.Dispose();
                // СТАЛО: timer1.Stop() предотвращает ObjectDisposedException
                timer1?.Stop();
                button11.Text = " Продолжить";
                button11.Image = Gpellers.Properties.Resources.icon_play;
                button11.TextAlign = ContentAlignment.MiddleRight;
                byte[] data = new byte[] { 0x82 };
                port?.Write(data, 0, data.Length);
            }
            else
            {
                isNotPaused = true;
                if (!sendDone)
                {
                    port?.Write("~");
                    isCounterCommand = false;
                    sendDone = false;
                }
                else
                {
                    port?.Write("~");
                    isCounterCommand = false;
                }
                timer1?.Start();
                button11.Text = "Пауза";
                button11.Image = Gpellers.Properties.Resources.icon_pause;
                button11.TextAlign = ContentAlignment.MiddleCenter;
            }
        }

        private void button9_Click_2(object sender, EventArgs e)
        {
            about about = new about();
            about.Show();
        }

        private void button12_Click(object sender, EventArgs e)
        {
            if (!(comboBox4.Text == ""))
            {
                string feed = (ParseFloatSafe(comboBox4.Text) * 60).ToString();
                string yCmd = "G01 Y " + startPosY.Text.ToString().Replace(",", ".") + " F" + feed;
                string xCmd = "G01 X " + startPosX.Text.ToString().Replace(",", ".") + " F" + feed;

                port?.Write("G90\n");
                AppendLog("G90");

                port?.Write(yCmd + "\n");
                isCounterCommand = false;
                AppendLog(yCmd);

                port?.Write(xCmd + "\n");
                AppendLog(xCmd);
                isCounterCommand = false;
            }
            else
            {
                MessageBox.Show("Укажите скорость");
            }
        }

        private void button10_Click_1(object sender, EventArgs e)
        {
            if (isNotStop == true)
            {
                isNotStop = false;
                isStreaming = false;
                button10.Text = "РАЗБЛОК.";
                // БЫЛО: timer1.Dispose();
                // СТАЛО: timer1.Stop() предотвращает ObjectDisposedException
                timer1?.Stop();
                byte[] data = new byte[] { 0x18 };
                port?.Write(data, 0, data.Length);
                isCounterCommand = false;
            }
            else
            {
                button10.Text = "СТОП";
                port?.Write("$X\n");
                isCounterCommand = false;
                timer1?.Start();
                isNotStop = true;
            }
        }

        private void button14_Click(object sender, EventArgs e)
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
            catch { return; }
        }

        private void button16_Click(object sender, EventArgs e)
        {
            if (!File.Exists(tempPath))
            {
                Task.Run(async () =>
                {
                    await BlinkButtonAsync(button16, TimeSpan.FromMilliseconds(250), 3, Color.Crimson);
                });
                MessageBox.Show("Сначала сгенерируйте код. Сохранение невозможно.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            string content = File.ReadAllText(tempPath);

            if (string.IsNullOrWhiteSpace(content))
            {
                Task.Run(async () =>
                {
                    await BlinkButtonAsync(button16, TimeSpan.FromMilliseconds(250), 3, Color.Crimson);
                });
                MessageBox.Show("Внутренний файл кода пуст, перегенерируйте код. Сохранение невозможно.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (SaveFileDialog saveDialog = new SaveFileDialog())
            {
                saveDialog.Title = "Сохранить G-code файл";
                saveDialog.Filter = "G-code файлы (*.gcode)|*.gcode|Все файлы (*.*)|*.*";
                saveDialog.DefaultExt = "gcode";
                saveDialog.AddExtension = true;
                saveDialog.FileName = "output.gcode";

                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        File.WriteAllText(saveDialog.FileName, content);
                        Task.Run(async () =>
                        {
                            await BlinkButtonAsync(button16, TimeSpan.FromMilliseconds(250), 3, Color.Green);
                        });
                        MessageBox.Show($"Файл успешно сохранён:\n{saveDialog.FileName}", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        Task.Run(async () =>
                        {
                            await BlinkButtonAsync(button16, TimeSpan.FromMilliseconds(250), 3, Color.Crimson);
                        });
                        MessageBox.Show($"Ошибка при сохранении файла:\n{ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void button17_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openDialog = new OpenFileDialog())
            {
                openDialog.Title = "Открыть G-code файл";
                openDialog.Filter = "G-code файлы (*.gcode)|*.gcode|Все файлы (*.*)|*.*";
                openDialog.CheckFileExists = true;

                if (openDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        string path = openDialog.FileName;
                        string content = File.ReadAllText(path);

                        if (string.IsNullOrWhiteSpace(content))
                        {
                            Task.Run(async () =>
                            {
                                await BlinkButtonAsync(button17, TimeSpan.FromMilliseconds(250), 3, Color.Crimson);
                            });
                            MessageBox.Show("Выбранный G-code файл пуст!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }

                        File.WriteAllText(tempPath, content);
                        double[] loaded = LoadCoordinates(tempPath);
                        startPosX.Text = loaded[0].ToString();
                        startPosY.Text = loaded[1].ToString();
                        endPosX.Text = loaded[2].ToString();
                        endPosY.Text = loaded[3].ToString();
                        comboBox6.Text = loaded[4].ToString();
                        comboBox4.Text = loaded[5].ToString();
                        comboBox2.Text = loaded[6].ToString();
                        Task.Run(async () =>
                        {
                            await BlinkButtonAsync(button17, TimeSpan.FromMilliseconds(250), 3, Color.Green);
                        });
                        MessageBox.Show(
                            $"Файл загружен\n{path}",
                            "Файл открыт", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        Task.Run(async () =>
                        {
                            await BlinkButtonAsync(button17, TimeSpan.FromMilliseconds(250), 3, Color.Crimson);
                        });
                        MessageBox.Show($"Ошибка при открытии файла:\n{ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void button13_Click(object sender, EventArgs e)
        {
            try
            {
                port?.Write("$H\n");
                isCounterCommand = false;
                AppendLog("$H");
            }
            catch { return; }
        }

        private void groupBox5_Enter(object sender, EventArgs e)
        {
        }

        private void comboBox7_SelectedIndexChanged(object sender, EventArgs e)
        {
        }
    }
}
