using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace G_write
{
    public partial class update : Form
    {
        public update()
        {
            InitializeComponent();
        }

        private async void update_Load(object sender, EventArgs e)
        {
            try
            {
                WebClient webClient = new WebClient();
                var client = new WebClient();
                String act_ver = webClient.DownloadString("https://github.com/Ilya-Arduino/GCodeWrite/blob/main/version.txt");
                if (!act_ver.Contains("1.0.6"))
                {
                    label2.Text = "Новая версия доступна на GitHub\nТекущая версия: 1.0.6\nПерейти на GitHub?";
                    button1.Enabled = true;
                }
                else
                {
                    label2.Text = "Обновлений нет";
                    button1.Enabled = false;
                }
                await Task.Delay(10);
           }
            catch
           {
               // button1.Enabled = false;
               // label2.Text = "Ошибка подключения.\nПовторите попытку позже.";
               // return;
           }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            var psi = new ProcessStartInfo("https://github.com/Ilya-Arduino/GCodeWrite/releases/tag/Taganrog")
            {
                UseShellExecute = true,
                Verb = "open"
            };
            Process.Start(psi);
            this.Close();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}

