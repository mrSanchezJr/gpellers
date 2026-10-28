using System;
using System.Drawing;
using System.Windows.Forms;

namespace G_write
{
    public class NumpadForm : Form
    {
        // События для взаимодействия с основной формой
        public event EventHandler<string> NumberPressed;
        public event EventHandler BackspacePressed;
        public event EventHandler EnterPressed;
        public event EventHandler ClearPressed;

        public NumpadForm()
        {
            InitializeForm();
            CreateButtons();
        }

        private void InitializeForm()
        {
           
            this.Text = "Экранная клавиатура";
            this.Size = new Size(200, 350); // Просто задаем размер
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.Manual;
            this.TopMost = true;
            this.ShowInTaskbar = false;
        }

        private void CreateButtons()
        {
            // Создаем кнопки с явными координатами и размерами

            // Первый ряд
            CreateButton("1", 10, 10, 50, 40);
            CreateButton("2", 65, 10, 50, 40);
            CreateButton("3", 120, 10, 50, 40);

            // Второй ряд
            CreateButton("4", 10, 55, 50, 40);
            CreateButton("5", 65, 55, 50, 40);
            CreateButton("6", 120, 55, 50, 40);

            // Третий ряд
            CreateButton("7", 10, 100, 50, 40);
            CreateButton("8", 65, 100, 50, 40);
            CreateButton("9", 120, 100, 50, 40);

            // Четвертый ряд
            CreateButton("0", 10, 145, 105, 40);  // Широкая кнопка 0
            CreateButton(".", 120, 145, 50, 40);

            // Пятый ряд
            CreateButton("←", 10, 190, 50, 40);   // Backspace
            CreateButton("Стереть", 65, 190, 105, 40);

            CreateButton("Ввод", 10, 240, 90, 40);// Clear
            //CreateButton("Enter", 120, 190, 50, 85); // Высокая кнопка Enter
        }

        private void CreateButton(string text, int x, int y, int width, int height)
        {
            var button = new Button
            {
                Text = text,
                Font = new Font("Arial", 10, FontStyle.Bold),
                Tag = text,
                Size = new Size(width, height),
                Location = new Point(x, y)
            };

            button.Click += Button_Click;
            this.Controls.Add(button);
        }

        private void InitializeComponent()
        {
            SuspendLayout();
            // 
            // NumpadForm
            // 
            ClientSize = new Size(278, 244);
            FormBorderStyle = FormBorderStyle.None;
            Name = "NumpadForm";
            Load += NumpadForm_Load;
            ResumeLayout(false);

        }

        private void Button_Click(object sender, EventArgs e)
        {
            var button = sender as Button;
            string tag = button.Tag as string;

            switch (tag)
            {
                case "←":
                    BackspacePressed?.Invoke(this, EventArgs.Empty);
                    break;
                case "Ввод":
                    EnterPressed?.Invoke(this, EventArgs.Empty);
                    break;
                case "Стереть":
                    ClearPressed?.Invoke(this, EventArgs.Empty);
                    break;
                default:
                    NumberPressed?.Invoke(this, tag);
                    break;
            }
        }

        private void NumpadForm_Load(object sender, EventArgs e)
        {

        }
    }
}