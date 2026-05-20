

using PicoPinnedArray;
using PicoStatus;
using PS5000AImports;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Net.Mime.MediaTypeNames;
using AllFurieTrans;
using WorkWithFile;

//using FileMFT;
namespace PS5000A
{
    public partial class PS5000ABlockForm : Form
    {

        private float cnc_x = 0;
        private float cnc_y = 0;


        private FileMFT fileMFT;
        private Switch Switch1;
        private bool stop_flag = false;
        private bool switch_connected = false;
        private double oscilloscope_timestep = 0;
        private string CODES = "ABCDEFGHIKJLMNOPQRSTUVWXYZ0123456789";

        private short _handle;
        public const int BUFFER_SIZE = 1024;
        public const int MAX_CHANNELS = 4;
        public const int QUAD_SCOPE = 4;
        public const int DUAL_SCOPE = 2;

        private ushort[] inputRanges = { 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000, 50000 };
        private bool _ready = false;
        //private ChannelSettings[] _channelSettings;
        private int _channelCount;
        private Imports.ps5000aBlockReady _callbackDelegate;
        private short[] minBuffersA;
        private short[] maxBuffersA;
        private long[] masA;
        private uint n = 10000;
        private double dt_ = 104 * 1.0E-9;
        private double[] arrA;

        private int _currentSampleRateHz = 0;
        private CancellationTokenSource _continuousCts;
        private readonly object _captureLock = new object();
        private System.Windows.Forms.Button _buttonContinuous;
        private System.Windows.Forms.CheckBox _checkBoxBroker;
        private System.Windows.Forms.TextBox _textBoxBrokerUrl;
        private System.Windows.Forms.Label _labelBrokerStatus;

        /*
         * частоты от 0 Гц 10Mhz
         * dt =10^-7
         * df = 100;
         */
        //private Complex[] Transform_dataA;



        public PS5000ABlockForm()
        {
            InitializeComponent();

            comboRangeA.DataSource = System.Enum.GetValues(typeof(Imports.Range));
            progressBar1.Text = "Готов к работе";
            timer1.Interval = 300;
            timer1.Tick += new EventHandler(Timer1_Tick);
            InitBrokerControls();
        }

        private int all, save = 0;
        private void Timer1_Tick(object Sender, EventArgs e)
        {
            progressBar1.Value = (int)(((double)save) / all * progressBar1.Maximum);
            Refresh();
        }

        //единственная дебажная версия
        //работает

        private void BlockCallback(short handle, short status, IntPtr pVoid)
        {// flag to say done reading data
            if (status != (short)StatusCodes.PICO_CANCELLED)
            {
                _ready = true;
            }
        }

        private uint SetTrigger(Imports.TriggerChannelProperties[] channelProperties,
            short nChannelProperties,
            Imports.TriggerConditions[] triggerConditions,
            short nTriggerConditions,
            Imports.ThresholdDirection[] directions,
            uint delay,
            short auxOutputEnabled,
            int autoTriggerMs)
        {
            uint status;

            status = Imports.SetTriggerChannelProperties(_handle, channelProperties, nChannelProperties, auxOutputEnabled,
                                                   autoTriggerMs);
            if (status != StatusCodes.PICO_OK)
            {
                return status;
            }

            status = Imports.SetTriggerChannelConditions(_handle, triggerConditions, nTriggerConditions);

            if (status != StatusCodes.PICO_OK)
            {
                return status;
            }
            if (directions == null)
            {
                directions = new Imports.ThresholdDirection[] { Imports.ThresholdDirection.None,
                                 Imports.ThresholdDirection.None, Imports.ThresholdDirection.None,
                                 Imports.ThresholdDirection.None, Imports.ThresholdDirection.None,
                                 Imports.ThresholdDirection.None };
            }
            status = Imports.SetTriggerChannelDirections(_handle, directions[(int)Imports.Channel.ChannelA],
                                                                  directions[(int)Imports.Channel.ChannelB],
                                                                  directions[(int)Imports.Channel.ChannelC],
                                                                  directions[(int)Imports.Channel.ChannelD],
                                                                  directions[(int)Imports.Channel.External],
                                                                  directions[(int)Imports.Channel.Aux]);
            if (status != StatusCodes.PICO_OK)
            {
                return status;
            }
            status = Imports.SetTriggerDelay(_handle, delay);
            if (status != StatusCodes.PICO_OK)
            {
                return status;
            }
            return status;
        }


        private void button1_Click(object sender, EventArgs e)
        {

            try
            {

                oscilloscope_timestep = double.Parse(textBox9.Text);
                if (oscilloscope_timestep < 4.0)
                {
                    throw new Exception();
                }

                oscilloscope_timestep = (oscilloscope_timestep - 3.0) / 62500000.0;

                StringBuilder UnitInfo = new StringBuilder(80);

                short handle;
                string[] description = {
                           "Driver Version    ",
                           "USB Version       ",
                           "Hardware Version  ",
                           "Variant Info      ",
                           "Serial            ",
                           "Cal Date          ",
                           "Kernel Ver        ",
                           "Digital Hardware  ",
                           "Analogue Hardware "
                         };

                Imports.DeviceResolution resolution = Imports.DeviceResolution.PS5000A_DR_16BIT;

                if (_handle > 0)
                {
                    Imports.CloseUnit(_handle);
                    _handle = 0;
                    button1.Text = "Open";
                }
                else
                {
                    uint status = Imports.OpenUnit(out handle, null, resolution);

                    if (handle > 0)
                    {
                        _handle = handle;

                        if (status == StatusCodes.PICO_POWER_SUPPLY_NOT_CONNECTED || status == StatusCodes.PICO_USB3_0_DEVICE_NON_USB3_0_PORT)
                        {
                            status = Imports.ChangePowerSource(_handle, status);
                        }
                        else if (status != StatusCodes.PICO_OK)
                        {
                            MessageBox.Show("Cannot open device error code: " + status.ToString(), "Error Opening Device", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            this.Close();
                        }
                        else
                        {
                            // Do nothing - power supply connected
                        }


                        button1.Text = "Закрыть";
                    }
                }
            }
            catch (Exception)
            {

                MessageBox.Show("Не удалось распознать шаг по времени");

            }
        }






        //private async void WriteDataAsync(Color color, double[] data, int page_num)
        //{
        //    await Task.Run(() => Visualase(Color.Blue, arrA, 1));
        //}

        private double[] LoadFromFile(string filename)
        {
            using (StreamReader Reader = new StreamReader(filename))
            {
                int l = int.Parse(Reader.ReadLine());
                double[] A = new double[l];
                for (int i = 0; i < l; i++)
                {
                    A[i] = double.Parse(Reader.ReadLine().Replace('.', ','));
                }
                Reader.Close();
                return A;
            }
        }


        private double[] ExtractFilterArgs(Complex[] f)
        {
            double[] r = new double[f.Length];
            for (int i = 0; i < f.Length; i++)
            {
                r[i] = f[i].Real;
            }
            return r;
        }

        private double[] ExtractFilterVals(Complex[] f)
        {
            double[] r = new double[f.Length];
            for (int i = 0; i < f.Length; i++)
            {
                r[i] = f[i].Imaginary;
            }
            return r;
        }

        private void SortFilterPoints(ref double[] x, ref double[] f)
        {
            int l = x.Length;
            for (int i = 0; i < l - 1; i++)
            {
                double xmin = x[i];
                int index = i;
                for (int j = i + 1; j < l; j++)
                {
                    if (xmin > f[j])
                    {
                        xmin = f[j];
                        index = j;
                    }
                }
                if (index != i)
                {
                    double x_ = x[index];
                    double f_ = f[index];
                    x[index] = x[i];
                    f[index] = f[i];
                    x[i] = x_;
                    f[i] = f_;
                }
            }
        }

        private bool visualising_now = false;

        private void Visualase(Color color, double[] data, int page_num)
        {
            if (!visualising_now)
            {
                Bitmap box = new Bitmap(tabControl1.TabPages[page_num].Width, tabControl1.TabPages[page_num].Height);
                Graphics g = Graphics.FromImage(box);

                visualising_now = true;
                //   tabControl1.TabPages[5].Invalidate();
                //Graphics e_ = tabControl1.TabPages[5].CreateGraphics();
                int sy = tabControl1.TabPages[page_num].Height;
                int sx = tabControl1.TabPages[page_num].Width;
                int l = data.Length;
                double mult = (double)sx / (double)l;
                Pen pp = new Pen(color);
                double max_abs = 0;
                //for (int i = 0; i < sx - 1; i++)
                for (int i = 0; i < l; i++)
                {
                    //if (max_abs < Math.Abs(data[(int)(i / mult)] - data[0]))
                    //{
                    //    max_abs = Math.Abs(data[(int)(i / mult)] - data[0]);
                    //}
                    //if (max_abs < Math.Abs(data[i] - data[0]))
                    //{
                    //    max_abs = Math.Abs(data[i] - data[0]);
                    //}
                    if (max_abs < Math.Abs(data[i]))
                    {
                        max_abs = Math.Abs(data[i]);
                    }
                }

                //for (int i = 0; i < l - 1; i++)
                //{
                //    int x0 = (int)((double)i * mult);
                //    int x1 = (int)((double)(i + 1) * mult);
                //    int y0 = (int)(data[i] / 65535.0 * (double)sy * 0.8 / 2 * 100 + (double)sy / 2.0);
                //    int y1 = (int)(data[i + 1] / 65535.0 * (double)sy * 0.8 / 2 * 100 + (double)sy / 2.0);
                //    e_.DrawLine(pp, x0, y0, x1, y1);
                //}
                //    for (int i = 0; i < sx - 1; i++)
                //{
                //    int x0 = (int)((double)i * mult);
                //    int x1 = (int)((double)(i + 1) * mult);
                //    int y0 = (int)((data[i] - 32768 )/ max_abs * (double)sy * 0.8 / 2.0  + (double)sy / 2.0);
                //    int y1 = (int)((data[i + 1] - 32768 )/ max_abs * (double)sy * 0.8 / 2.0  + (double)sy / 2.0);
                //    e_.DrawLine(pp, x0, y0, x1, y1);
                //}
                //if (max_abs != 0)
                //{
                //    for (int i = 0; i < sx - 1; i++)
                //    {
                //        int x0 = i;
                //        int x1 = i + 1;
                //        int y0 = (int)((data[(int)(i / mult)] - data[0]) / max_abs * (double)sy * 0.8 / 2.0 + (double)sy / 2.0);
                //        int y1 = (int)((data[(int)((i + 1) / mult)] - data[0]) / max_abs * (double)sy * 0.8 / 2.0 + (double)sy / 2.0);
                //        g.DrawLine(pp, x0, y0, x1, y1);
                //    }
                //}
                //pictureBox1.Image = box;
                int di = 20;
                double max_ = 0;
                if (max_abs != 0)
                {
                    for (int i = 0; i < l - 1 - di; i += di)
                    {
                        double VVV = (double)data[i];
                        if ((VVV * VVV) > (max_ * max_))
                        {
                            // найден больший элемент
                            max_ = VVV;
                        }
                    }
                    if (max_ < 0)
                    {
                        max_ = -max_;
                    }

                    chart1.Hide();

                    chart1.ChartAreas[0].AxisX.Minimum = -oscilloscope_timestep * double.Parse(textBox13.Text);
                    chart1.ChartAreas[0].AxisX.Maximum = oscilloscope_timestep * double.Parse(textBox10.Text);
                    chart1.ChartAreas[0].AxisY.Maximum = max_ * 1000.0;// inputRanges[comboRangeA.SelectedIndex] / 65000.0;
                    chart1.ChartAreas[0].AxisY.Minimum = -max_ * 1000.0;//  - inputRanges[comboRangeA.SelectedIndex] / 65000.0;
                    // Определяем шаг сетки
                    chart1.ChartAreas[0].AxisX.MajorGrid.Interval = oscilloscope_timestep * (double.Parse(textBox13.Text) + double.Parse(textBox10.Text)) / 10.0;


                    chart1.Series["Series1"].Points.Clear();
                    ///    DataPointCollection 
                    for (int i = 0; i < l - 1 - di; i += di)
                    {
                        //int x0 = (int)((double)i * mult);
                        //int x1 = (int)((double)(i + di) * mult);
                        //int y0 = sy - ((int)(data[i] / max_ * (double)sy * 0.8 / 2 + (double)sy / 2.0));
                        //int y1 = sy - ((int)(data[i + di] / max_ * (double)sy * 0.8 / 2 + (double)sy / 2.0));

                        //g.DrawLine(pp, x0, y0, x1, y1);
                        //if ( save!= uint.Parse(textBox11.Text)) { 
                        //chart1.Series["Series1"].Points.AddXY(x0 * 16e-9 / mult, (double) data[i] / (double)save);
                        //}
                        //else
                        //{

                        //}

                        chart1.Series["Series1"].Points.AddXY((double)i * 16e-9, (double)data[i] * 1000.0);
                        //  chart1.Series["Series1"].Points
                    }
                    //  chart1.Invalidate();
                    // chart1.Series["Series1"].Points.ResumeUpdates();


                }
                using (MemoryStream ms = new MemoryStream())
                {
                    chart1.SaveImage(ms, System.Windows.Forms.DataVisualization.Charting.ChartImageFormat.Bmp);
                    Bitmap bm = new Bitmap(ms);
                    pictureBox1.Image = bm; // Clipboard.SetImage(bm);
                }
                // pictureBox1.Image = box;
                //   pictureBox1.Image = chart1 
            }
            //Brush ff;
            //e_.FillPolygon()
            //Graphics g = this.CreateGraphics();
            //g.DrawLine(new Pen(Color.Red), 0, 0, 100, 100);

            visualising_now = false;
        }


        private async void VisualaseAsync(Color color, double[] data, int page_num)
        {
            await Task.Run(() => Visualase(Color.Blue, arrA, 1));
        }
        //private void Visualase(Color color, long[] data)
        //{
        //    //tabControl1.TabPages[5].Invalidate();
        //    Graphics e_ = tabControl1.TabPages[5].CreateGraphics();
        //    int sy = tabControl1.TabPages[5].Height;
        //    int sx = tabControl1.TabPages[5].Width;
        //    int l = data.Length;
        //    double mult = (double)sx / (double)l;
        //    Pen pp = new Pen(color);
        //    double max_abs = 0;

        //    for (int i = 0; i < sx - 1; i++)
        //    {
        //        if (max_abs < Math.Abs(data[(int)(i / mult)] - data[0]))
        //        {
        //            max_abs = Math.Abs(data[(int)(i / mult)] - data[0]);
        //        }
        //    }

        //    //for (int i = 0; i < l - 1; i++)
        //    //{
        //    //    if (max_abs < Math.Abs(data[i] - data[0]))
        //    //        max_abs = Math.Abs(data[i] - data[0]);
        //    //}


        //    for (int i = 0; i < sx - 1; i++)
        //    {
        //        int x0 = i;
        //        int x1 = i + 1;
        //        int y0 = (int)((data[(int)(i / mult)] - data[0]) / max_abs * (double)sy * 0.8 / 2.0 + (double)sy / 2.0);
        //        int y1 = (int)((data[(int)((i + 1) / mult)] - data[0]) / max_abs * (double)sy * 0.8 / 2.0 + (double)sy / 2.0);
        //        e_.DrawLine(pp, x0, y0, x1, y1);
        //    }
        //}
        private void Visualase(Color color, long[] data)
        {
            if (!visualising_now)
            {
                double[] dadada = new double[data.Length];
                for (int i = 0; i < data.Length; i++)
                {
                    dadada[i] = (double)data[i];
                }
                Visualase(color, dadada, 5);
            }
        }

        //Самый времяемкий метод

        private void start(uint sampleCountBefore = 50000, uint sampleCountAfter = 50000, int write_every = 100,
            Imports.Range? rangeOverride = null, bool? bwFilterOverride = null, uint? timebaseOverride = null)
        {
            uint all_ = sampleCountAfter + sampleCountBefore;
            uint status;
            int ms;
            status = Imports.MemorySegments(_handle, 1, out ms);
            Imports.Range IR = rangeOverride ?? (Imports.Range)comboRangeA.SelectedIndex;
            status = Imports.SetChannel(_handle, Imports.Channel.ChannelA, 1, Imports.Coupling.PS5000A_AC, IR, 0);
            // Voltage_Range = 200;
            //  status = Imports.SetChannel(_handle, Imports.Channel.ChannelA, 1, Imports.Coupling.PS5000A_AC, Imports.Range.Range_200mV, 0);
            //status = Imports.SetChannel(_handle, Imports.Channel.ChannelA, 1, Imports.Coupling.PS5000A_DC, Imports.Range.Range_200mV, 0);

            const short enable = 1;
            const uint delay = 0;
            const short threshold = 20000;
            const short auto = 22222;
            if (bwFilterOverride ?? checkBox1.Checked)
            {
                status = Imports.SetBandwidthFilter(_handle, Imports.Channel.ChannelA, Imports.BandwidthLimiter.PS5000A_BW_20MHZ);
            }
            status = Imports.SetSimpleTrigger(_handle, enable, Imports.Channel.External, threshold, Imports.ThresholdDirection.Rising, delay, auto);
            _ready = false;
            _callbackDelegate = BlockCallback;
            _channelCount = 1;
            //string data;
            


            bool retry;

            PinnedArray<short>[] minPinned = new PinnedArray<short>[_channelCount];
            PinnedArray<short>[] maxPinned = new PinnedArray<short>[_channelCount];

            int timeIndisposed;
            minBuffersA = new short[all_];
            maxBuffersA = new short[all_];
            minPinned[0] = new PinnedArray<short>(minBuffersA);
            maxPinned[0] = new PinnedArray<short>(maxBuffersA);
            status = Imports.SetDataBuffers(_handle, Imports.Channel.ChannelA, maxBuffersA, minBuffersA, (int)sampleCountAfter + (int)sampleCountBefore, 0, Imports.RatioMode.None);


            uint tb = timebaseOverride ?? uint.Parse(this.textBox9.Text);
            {
                float intervalNs;
                int maxSamp;
                if (Imports.GetTimebase2(_handle, tb, (int)all_, out intervalNs, out maxSamp, 0) == StatusCodes.PICO_OK && intervalNs > 0)
                    _currentSampleRateHz = (int)Math.Round(1e9 / intervalNs);
            }

            _ready = false;
            _callbackDelegate = BlockCallback;
            do
            {
                retry = false;
                status = Imports.RunBlock(_handle, (int)sampleCountBefore, (int)sampleCountAfter, tb, out timeIndisposed, 0, _callbackDelegate, IntPtr.Zero);
                if (status == (short)StatusCodes.PICO_POWER_SUPPLY_CONNECTED || status == (short)StatusCodes.PICO_POWER_SUPPLY_NOT_CONNECTED || status == (short)StatusCodes.PICO_POWER_SUPPLY_UNDERVOLTAGE)
                {
                    status = Imports.ChangePowerSource(_handle, status);
                    retry = true;
                }
                else
                {
                    //  textMessage.AppendText("Run Block Called\n");
                }
            }
            while (retry);
            while (!_ready)
            {
                //    Thread.Sleep(1);
                //  Application.DoEvents();///тормоза
                Thread.Sleep(0);
            }
            Imports.Stop(_handle);
            if (_ready)
            {
                short overflow;
                status = Imports.GetValues(_handle, 0, ref all_, 1, Imports.RatioMode.None, 0, out overflow);

                if (status == (short)StatusCodes.PICO_OK)
                {
                    //for (x = 0; x < all_; x++)
                    //{
                    //    masA[x] += maxBuffersA[x] + minBuffersA[x];//=========================================================!
                    //}


                    for (int x = 0; x < all_ / 4; x++)
                    {
                        masA[4 * x] += maxBuffersA[4 * x] + minBuffersA[4 * x];
                        masA[4 * x + 1] += maxBuffersA[4 * x + 1] + minBuffersA[4 * x + 1];
                        masA[4 * x + 2] += maxBuffersA[4 * x + 2] + minBuffersA[4 * x + 2];
                        masA[4 * x + 3] += maxBuffersA[4 * x + 3] + minBuffersA[4 * x + 3];
                    }

                    for (int x = 0; x < (all_ % 4); x++)
                    {
                        uint offset = 4 * (all_ / 4);
                        masA[offset + x] += maxBuffersA[offset + x] + minBuffersA[offset + x];
                    }
                }
            }

            Imports.Stop(_handle);
            foreach (PinnedArray<short> p in minPinned)
            {
                if (p != null)
                {
                    p.Dispose();
                }
            }
            foreach (PinnedArray<short> p in maxPinned)
            {
                if (p != null)
                {
                    p.Dispose();
                }
            }

            // Application.DoEvents(); ///тормоза
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.Windows.Forms.DataVisualization.Charting.ChartArea chartArea1 = new System.Windows.Forms.DataVisualization.Charting.ChartArea();
            System.Windows.Forms.DataVisualization.Charting.Legend legend1 = new System.Windows.Forms.DataVisualization.Charting.Legend();
            System.Windows.Forms.DataVisualization.Charting.Series series1 = new System.Windows.Forms.DataVisualization.Charting.Series();
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabPage1 = new System.Windows.Forms.TabPage();
            this.label26 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.comboRangeA = new System.Windows.Forms.ComboBox();
            this.textBox9 = new System.Windows.Forms.TextBox();
            this.label13 = new System.Windows.Forms.Label();
            this.button1 = new System.Windows.Forms.Button();
            this.tabPage3 = new System.Windows.Forms.TabPage();
            this.button16 = new System.Windows.Forms.Button();
            this.label30 = new System.Windows.Forms.Label();
            this.textBox21 = new System.Windows.Forms.TextBox();
            this.button13 = new System.Windows.Forms.Button();
            this.checkedListBox2 = new System.Windows.Forms.CheckedListBox();
            this.checkedListBox1 = new System.Windows.Forms.CheckedListBox();
            this.textBoxUnitInfo = new System.Windows.Forms.TextBox();
            this.listBox1 = new System.Windows.Forms.ListBox();
            this.button7 = new System.Windows.Forms.Button();
            this.button6 = new System.Windows.Forms.Button();
            this.label16 = new System.Windows.Forms.Label();
            this.textBox1 = new System.Windows.Forms.TextBox();
            this.label12 = new System.Windows.Forms.Label();
            this.label11 = new System.Windows.Forms.Label();
            this.label10 = new System.Windows.Forms.Label();
            this.button5 = new System.Windows.Forms.Button();
            this.label9 = new System.Windows.Forms.Label();
            this.label8 = new System.Windows.Forms.Label();
            this.label7 = new System.Windows.Forms.Label();
            this.label6 = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.button4 = new System.Windows.Forms.Button();
            this.button3 = new System.Windows.Forms.Button();
            this.tabPage2 = new System.Windows.Forms.TabPage();
            this.button14 = new System.Windows.Forms.Button();
            this.label28 = new System.Windows.Forms.Label();
            this.textBox20 = new System.Windows.Forms.TextBox();
            this.textBox19 = new System.Windows.Forms.TextBox();
            this.label27 = new System.Windows.Forms.Label();
            this.checkBox13 = new System.Windows.Forms.CheckBox();
            this.checkBox10 = new System.Windows.Forms.CheckBox();
            this.textBox6 = new System.Windows.Forms.TextBox();
            this.textBox5 = new System.Windows.Forms.TextBox();
            this.textBox4 = new System.Windows.Forms.TextBox();
            this.label22 = new System.Windows.Forms.Label();
            this.label19 = new System.Windows.Forms.Label();
            this.label18 = new System.Windows.Forms.Label();
            this.checkBox6 = new System.Windows.Forms.CheckBox();
            this.checkBox5 = new System.Windows.Forms.CheckBox();
            this.label17 = new System.Windows.Forms.Label();
            this.textBox3 = new System.Windows.Forms.TextBox();
            this.checkBox2 = new System.Windows.Forms.CheckBox();
            this.button11 = new System.Windows.Forms.Button();
            this.checkBox20 = new System.Windows.Forms.CheckBox();
            this.button8 = new System.Windows.Forms.Button();
            this.checkBox1 = new System.Windows.Forms.CheckBox();
            this.textBox14 = new System.Windows.Forms.TextBox();
            this.label21 = new System.Windows.Forms.Label();
            this.textBox13 = new System.Windows.Forms.TextBox();
            this.label20 = new System.Windows.Forms.Label();
            this.label15 = new System.Windows.Forms.Label();
            this.textBox11 = new System.Windows.Forms.TextBox();
            this.textBox10 = new System.Windows.Forms.TextBox();
            this.label14 = new System.Windows.Forms.Label();
            this.button2 = new System.Windows.Forms.Button();
            this.tabPage4 = new System.Windows.Forms.TabPage();
            this.textBox16 = new System.Windows.Forms.TextBox();
            this.checkBox9 = new System.Windows.Forms.CheckBox();
            this.checkBox8 = new System.Windows.Forms.CheckBox();
            this.checkBox4 = new System.Windows.Forms.CheckBox();
            this.checkBox3 = new System.Windows.Forms.CheckBox();
            this.button10 = new System.Windows.Forms.Button();
            this.textBox2 = new System.Windows.Forms.TextBox();
            this.button9 = new System.Windows.Forms.Button();
            this.tabPage5 = new System.Windows.Forms.TabPage();
            this.checkBox12 = new System.Windows.Forms.CheckBox();
            this.textBox18 = new System.Windows.Forms.TextBox();
            this.checkBox11 = new System.Windows.Forms.CheckBox();
            this.textBox17 = new System.Windows.Forms.TextBox();
            this.textBox12 = new System.Windows.Forms.TextBox();
            this.label25 = new System.Windows.Forms.Label();
            this.checkBox7 = new System.Windows.Forms.CheckBox();
            this.button12 = new System.Windows.Forms.Button();
            this.textBox8 = new System.Windows.Forms.TextBox();
            this.label24 = new System.Windows.Forms.Label();
            this.label23 = new System.Windows.Forms.Label();
            this.textBox7 = new System.Windows.Forms.TextBox();
            this.tabPage6 = new System.Windows.Forms.TabPage();
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.tabPage7 = new System.Windows.Forms.TabPage();
            this.chart1 = new System.Windows.Forms.DataVisualization.Charting.Chart();
            this.tabPage8 = new System.Windows.Forms.TabPage();
            this.button15 = new System.Windows.Forms.Button();
            this.tabPage9 = new System.Windows.Forms.TabPage();
            this.checkBox14 = new System.Windows.Forms.CheckBox();
            this.button17 = new System.Windows.Forms.Button();
            this.label29 = new System.Windows.Forms.Label();
            this.textBox22 = new System.Windows.Forms.TextBox();
            this.tabPage10 = new System.Windows.Forms.TabPage();
            this.button25 = new System.Windows.Forms.Button();
            this.button24 = new System.Windows.Forms.Button();
            this.button23 = new System.Windows.Forms.Button();
            this.button22 = new System.Windows.Forms.Button();
            this.textBox28 = new System.Windows.Forms.TextBox();
            this.label35 = new System.Windows.Forms.Label();
            this.textBox29 = new System.Windows.Forms.TextBox();
            this.label36 = new System.Windows.Forms.Label();
            this.label34 = new System.Windows.Forms.Label();
            this.textBox27 = new System.Windows.Forms.TextBox();
            this.textBox26 = new System.Windows.Forms.TextBox();
            this.button21 = new System.Windows.Forms.Button();
            this.listBox2 = new System.Windows.Forms.ListBox();
            this.button19 = new System.Windows.Forms.Button();
            this.button20 = new System.Windows.Forms.Button();
            this.textBox25 = new System.Windows.Forms.TextBox();
            this.label33 = new System.Windows.Forms.Label();
            this.textBox24 = new System.Windows.Forms.TextBox();
            this.label32 = new System.Windows.Forms.Label();
            this.textBox23 = new System.Windows.Forms.TextBox();
            this.label31 = new System.Windows.Forms.Label();
            this.button18 = new System.Windows.Forms.Button();
            this.textBox15 = new System.Windows.Forms.TextBox();
            this.tabPage11 = new System.Windows.Forms.TabPage();
            this.checkBox17 = new System.Windows.Forms.CheckBox();
            this.checkBox16 = new System.Windows.Forms.CheckBox();
            this.textBox38 = new System.Windows.Forms.TextBox();
            this.label45 = new System.Windows.Forms.Label();
            this.textBox36 = new System.Windows.Forms.TextBox();
            this.label43 = new System.Windows.Forms.Label();
            this.textBox37 = new System.Windows.Forms.TextBox();
            this.label44 = new System.Windows.Forms.Label();
            this.checkBox15 = new System.Windows.Forms.CheckBox();
            this.button26 = new System.Windows.Forms.Button();
            this.textBox34 = new System.Windows.Forms.TextBox();
            this.label41 = new System.Windows.Forms.Label();
            this.textBox35 = new System.Windows.Forms.TextBox();
            this.label42 = new System.Windows.Forms.Label();
            this.textBox32 = new System.Windows.Forms.TextBox();
            this.label39 = new System.Windows.Forms.Label();
            this.textBox33 = new System.Windows.Forms.TextBox();
            this.label40 = new System.Windows.Forms.Label();
            this.textBox30 = new System.Windows.Forms.TextBox();
            this.label37 = new System.Windows.Forms.Label();
            this.textBox31 = new System.Windows.Forms.TextBox();
            this.label38 = new System.Windows.Forms.Label();
            this.progressBar1 = new System.Windows.Forms.ProgressBar();
            this.timer1 = new System.Windows.Forms.Timer(this.components);
            this.folderBrowserDialog1 = new System.Windows.Forms.FolderBrowserDialog();
            this.timer2 = new System.Windows.Forms.Timer(this.components);
            this.tabControl1.SuspendLayout();
            this.tabPage1.SuspendLayout();
            this.tabPage3.SuspendLayout();
            this.tabPage2.SuspendLayout();
            this.tabPage4.SuspendLayout();
            this.tabPage5.SuspendLayout();
            this.tabPage6.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.tabPage7.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.chart1)).BeginInit();
            this.tabPage8.SuspendLayout();
            this.tabPage9.SuspendLayout();
            this.tabPage10.SuspendLayout();
            this.tabPage11.SuspendLayout();
            this.SuspendLayout();
            // 
            // tabControl1
            // 
            this.tabControl1.Controls.Add(this.tabPage1);
            this.tabControl1.Controls.Add(this.tabPage3);
            this.tabControl1.Controls.Add(this.tabPage2);
            this.tabControl1.Controls.Add(this.tabPage4);
            this.tabControl1.Controls.Add(this.tabPage5);
            this.tabControl1.Controls.Add(this.tabPage6);
            this.tabControl1.Controls.Add(this.tabPage7);
            this.tabControl1.Controls.Add(this.tabPage8);
            this.tabControl1.Controls.Add(this.tabPage9);
            this.tabControl1.Controls.Add(this.tabPage10);
            this.tabControl1.Controls.Add(this.tabPage11);
            this.tabControl1.Font = new System.Drawing.Font("Microsoft Sans Serif", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.tabControl1.Location = new System.Drawing.Point(3, 2);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(699, 376);
            this.tabControl1.TabIndex = 0;
            this.tabControl1.SelectedIndexChanged += new System.EventHandler(this.tabControl1_SelectedIndexChanged);
            // 
            // tabPage1
            // 
            this.tabPage1.Controls.Add(this.label26);
            this.tabPage1.Controls.Add(this.label1);
            this.tabPage1.Controls.Add(this.comboRangeA);
            this.tabPage1.Controls.Add(this.textBox9);
            this.tabPage1.Controls.Add(this.label13);
            this.tabPage1.Controls.Add(this.button1);
            this.tabPage1.Location = new System.Drawing.Point(4, 27);
            this.tabPage1.Name = "tabPage1";
            this.tabPage1.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage1.Size = new System.Drawing.Size(691, 345);
            this.tabPage1.TabIndex = 0;
            this.tabPage1.Text = "Подключение";
            this.tabPage1.UseVisualStyleBackColor = true;
            // 
            // label26
            // 
            this.label26.AutoSize = true;
            this.label26.Location = new System.Drawing.Point(265, 60);
            this.label26.Name = "label26";
            this.label26.Size = new System.Drawing.Size(166, 18);
            this.label26.TabIndex = 27;
            this.label26.Text = "Рекомендуется 100мВ";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.label1.Location = new System.Drawing.Point(294, 28);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(73, 16);
            this.label1.TabIndex = 26;
            this.label1.Text = "Диапазон";
            // 
            // comboRangeA
            // 
            this.comboRangeA.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.comboRangeA.FormattingEnabled = true;
            this.comboRangeA.Location = new System.Drawing.Point(434, 25);
            this.comboRangeA.Name = "comboRangeA";
            this.comboRangeA.Size = new System.Drawing.Size(121, 24);
            this.comboRangeA.TabIndex = 25;
            // 
            // textBox9
            // 
            this.textBox9.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.textBox9.Location = new System.Drawing.Point(126, 121);
            this.textBox9.Name = "textBox9";
            this.textBox9.Size = new System.Drawing.Size(100, 22);
            this.textBox9.TabIndex = 16;
            this.textBox9.Text = "4";
            this.textBox9.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // label13
            // 
            this.label13.AutoSize = true;
            this.label13.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.label13.Location = new System.Drawing.Point(25, 124);
            this.label13.Name = "label13";
            this.label13.Size = new System.Drawing.Size(64, 16);
            this.label13.TabIndex = 15;
            this.label13.Text = "timebase";
            this.label13.Click += new System.EventHandler(this.label13_Click);
            // 
            // button1
            // 
            this.button1.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.button1.Location = new System.Drawing.Point(17, 18);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(164, 40);
            this.button1.TabIndex = 0;
            this.button1.Text = "Подключиться";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // tabPage3
            // 
            this.tabPage3.Controls.Add(this.button16);
            this.tabPage3.Controls.Add(this.label30);
            this.tabPage3.Controls.Add(this.textBox21);
            this.tabPage3.Controls.Add(this.button13);
            this.tabPage3.Controls.Add(this.checkedListBox2);
            this.tabPage3.Controls.Add(this.checkedListBox1);
            this.tabPage3.Controls.Add(this.textBoxUnitInfo);
            this.tabPage3.Controls.Add(this.listBox1);
            this.tabPage3.Controls.Add(this.button7);
            this.tabPage3.Controls.Add(this.button6);
            this.tabPage3.Controls.Add(this.label16);
            this.tabPage3.Controls.Add(this.textBox1);
            this.tabPage3.Controls.Add(this.label12);
            this.tabPage3.Controls.Add(this.label11);
            this.tabPage3.Controls.Add(this.label10);
            this.tabPage3.Controls.Add(this.button5);
            this.tabPage3.Controls.Add(this.label9);
            this.tabPage3.Controls.Add(this.label8);
            this.tabPage3.Controls.Add(this.label7);
            this.tabPage3.Controls.Add(this.label6);
            this.tabPage3.Controls.Add(this.label5);
            this.tabPage3.Controls.Add(this.label4);
            this.tabPage3.Controls.Add(this.label3);
            this.tabPage3.Controls.Add(this.label2);
            this.tabPage3.Controls.Add(this.button4);
            this.tabPage3.Controls.Add(this.button3);
            this.tabPage3.Location = new System.Drawing.Point(4, 27);
            this.tabPage3.Name = "tabPage3";
            this.tabPage3.Size = new System.Drawing.Size(691, 345);
            this.tabPage3.TabIndex = 2;
            this.tabPage3.Text = "Коммутация";
            this.tabPage3.UseVisualStyleBackColor = true;
            // 
            // button16
            // 
            this.button16.Location = new System.Drawing.Point(13, 287);
            this.button16.Name = "button16";
            this.button16.Size = new System.Drawing.Size(212, 36);
            this.button16.TabIndex = 42;
            this.button16.Text = "Отключить все";
            this.button16.UseVisualStyleBackColor = true;
            this.button16.Click += new System.EventHandler(this.button16_Click);
            // 
            // label30
            // 
            this.label30.AutoSize = true;
            this.label30.Location = new System.Drawing.Point(10, 86);
            this.label30.Name = "label30";
            this.label30.Size = new System.Drawing.Size(62, 18);
            this.label30.TabIndex = 40;
            this.label30.Text = "СЛОЕВ";
            // 
            // textBox21
            // 
            this.textBox21.Location = new System.Drawing.Point(79, 80);
            this.textBox21.Name = "textBox21";
            this.textBox21.Size = new System.Drawing.Size(146, 24);
            this.textBox21.TabIndex = 41;
            this.textBox21.Text = "5";
            // 
            // button13
            // 
            this.button13.Location = new System.Drawing.Point(433, 293);
            this.button13.Name = "button13";
            this.button13.Size = new System.Drawing.Size(250, 32);
            this.button13.TabIndex = 39;
            this.button13.Text = "Инвертировать выбор";
            this.button13.UseVisualStyleBackColor = true;
            this.button13.Click += new System.EventHandler(this.button13_Click_1);
            // 
            // checkedListBox2
            // 
            this.checkedListBox2.FormattingEnabled = true;
            this.checkedListBox2.Items.AddRange(new object[] {
            "0",
            "1",
            "2",
            "3",
            "4",
            "5",
            "6",
            "7",
            "8",
            "9",
            "10",
            "11",
            "12",
            "13",
            "14",
            "15",
            "16",
            "17",
            "18",
            "19",
            "20",
            "21",
            "22",
            "23",
            "24",
            "25",
            "26",
            "27",
            "28",
            "29",
            "30",
            "31",
            "32",
            "33",
            "34",
            "35",
            "36",
            "37",
            "38",
            "39",
            "40",
            "41",
            "42",
            "43",
            "44",
            "45",
            "46",
            "47",
            "48",
            "49",
            "50",
            "51",
            "52",
            "53",
            "54",
            "55"});
            this.checkedListBox2.Location = new System.Drawing.Point(613, 68);
            this.checkedListBox2.Name = "checkedListBox2";
            this.checkedListBox2.Size = new System.Drawing.Size(70, 194);
            this.checkedListBox2.TabIndex = 38;
            // 
            // checkedListBox1
            // 
            this.checkedListBox1.FormattingEnabled = true;
            this.checkedListBox1.Items.AddRange(new object[] {
            "0",
            "1",
            "2",
            "3",
            "4",
            "5",
            "6",
            "7",
            "8",
            "9",
            "10",
            "11",
            "12",
            "13",
            "14",
            "15",
            "16",
            "17",
            "18",
            "19",
            "20",
            "21",
            "22",
            "23",
            "24",
            "25",
            "26",
            "27",
            "28",
            "29",
            "30",
            "31",
            "32",
            "33",
            "34",
            "35",
            "36",
            "37",
            "38",
            "39",
            "40",
            "41",
            "42",
            "43",
            "44",
            "45",
            "46",
            "47",
            "48",
            "49",
            "50",
            "51",
            "52",
            "53",
            "54",
            "55"});
            this.checkedListBox1.Location = new System.Drawing.Point(520, 68);
            this.checkedListBox1.Name = "checkedListBox1";
            this.checkedListBox1.Size = new System.Drawing.Size(64, 194);
            this.checkedListBox1.TabIndex = 37;
            // 
            // textBoxUnitInfo
            // 
            this.textBoxUnitInfo.Location = new System.Drawing.Point(231, 18);
            this.textBoxUnitInfo.Multiline = true;
            this.textBoxUnitInfo.Name = "textBoxUnitInfo";
            this.textBoxUnitInfo.Size = new System.Drawing.Size(184, 307);
            this.textBoxUnitInfo.TabIndex = 36;
            // 
            // listBox1
            // 
            this.listBox1.FormattingEnabled = true;
            this.listBox1.ItemHeight = 18;
            this.listBox1.Location = new System.Drawing.Point(143, 16);
            this.listBox1.Name = "listBox1";
            this.listBox1.Size = new System.Drawing.Size(82, 40);
            this.listBox1.TabIndex = 35;
            // 
            // button7
            // 
            this.button7.Location = new System.Drawing.Point(13, 46);
            this.button7.Name = "button7";
            this.button7.Size = new System.Drawing.Size(124, 28);
            this.button7.TabIndex = 34;
            this.button7.Text = "Подключить порт";
            this.button7.UseVisualStyleBackColor = true;
            this.button7.Click += new System.EventHandler(this.button7_Click);
            // 
            // button6
            // 
            this.button6.Location = new System.Drawing.Point(13, 16);
            this.button6.Name = "button6";
            this.button6.Size = new System.Drawing.Size(124, 29);
            this.button6.TabIndex = 33;
            this.button6.Text = "Список портов";
            this.button6.UseVisualStyleBackColor = true;
            this.button6.Click += new System.EventHandler(this.button6_Click);
            // 
            // label16
            // 
            this.label16.AutoSize = true;
            this.label16.Location = new System.Drawing.Point(436, 29);
            this.label16.Name = "label16";
            this.label16.Size = new System.Drawing.Size(247, 18);
            this.label16.TabIndex = 32;
            this.label16.Text = "Роли датчиков в автоматическом";
            // 
            // textBox1
            // 
            this.textBox1.Location = new System.Drawing.Point(14, 128);
            this.textBox1.Name = "textBox1";
            this.textBox1.Size = new System.Drawing.Size(211, 24);
            this.textBox1.TabIndex = 31;
            // 
            // label12
            // 
            this.label12.AutoSize = true;
            this.label12.Location = new System.Drawing.Point(11, 107);
            this.label12.Name = "label12";
            this.label12.Size = new System.Drawing.Size(174, 18);
            this.label12.TabIndex = 30;
            this.label12.Text = "Введите номер датчика";
            // 
            // label11
            // 
            this.label11.AutoSize = true;
            this.label11.Location = new System.Drawing.Point(600, 47);
            this.label11.Name = "label11";
            this.label11.Size = new System.Drawing.Size(83, 18);
            this.label11.TabIndex = 29;
            this.label11.Text = "Источники";
            // 
            // label10
            // 
            this.label10.AutoSize = true;
            this.label10.Location = new System.Drawing.Point(498, 49);
            this.label10.Name = "label10";
            this.label10.Size = new System.Drawing.Size(86, 18);
            this.label10.TabIndex = 28;
            this.label10.Text = "Приемники";
            // 
            // button5
            // 
            this.button5.Location = new System.Drawing.Point(14, 238);
            this.button5.Name = "button5";
            this.button5.Size = new System.Drawing.Size(211, 36);
            this.button5.TabIndex = 27;
            this.button5.Text = "Отключить";
            this.button5.UseVisualStyleBackColor = true;
            this.button5.Click += new System.EventHandler(this.button5_Click);
            // 
            // label9
            // 
            this.label9.AutoSize = true;
            this.label9.Location = new System.Drawing.Point(430, 206);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(86, 18);
            this.label9.TabIndex = 26;
            this.label9.Text = "Датчик H\\7";
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Location = new System.Drawing.Point(430, 184);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(87, 18);
            this.label8.TabIndex = 25;
            this.label8.Text = "Датчик G\\6";
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(430, 164);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(84, 18);
            this.label7.TabIndex = 24;
            this.label7.Text = "Датчик F\\5";
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(430, 146);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(85, 18);
            this.label6.TabIndex = 23;
            this.label6.Text = "Датчик E\\4";
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(430, 128);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(86, 18);
            this.label5.TabIndex = 22;
            this.label5.Text = "Датчик D\\3";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(430, 109);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(86, 18);
            this.label4.TabIndex = 21;
            this.label4.Text = "Датчик C\\2";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(430, 91);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(85, 18);
            this.label3.TabIndex = 20;
            this.label3.Text = "Датчик B\\1";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(430, 70);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(84, 18);
            this.label2.TabIndex = 19;
            this.label2.Text = "Датчик A\\0";
            // 
            // button4
            // 
            this.button4.Location = new System.Drawing.Point(14, 198);
            this.button4.Name = "button4";
            this.button4.Size = new System.Drawing.Size(211, 34);
            this.button4.TabIndex = 18;
            this.button4.Text = "Подключить как приемник";
            this.button4.UseVisualStyleBackColor = true;
            this.button4.Click += new System.EventHandler(this.button4_Click);
            // 
            // button3
            // 
            this.button3.Location = new System.Drawing.Point(14, 158);
            this.button3.Name = "button3";
            this.button3.Size = new System.Drawing.Size(211, 34);
            this.button3.TabIndex = 17;
            this.button3.Text = "Подключить как источник";
            this.button3.UseVisualStyleBackColor = true;
            this.button3.Click += new System.EventHandler(this.button3_Click);
            // 
            // tabPage2
            // 
            this.tabPage2.Controls.Add(this.button14);
            this.tabPage2.Controls.Add(this.label28);
            this.tabPage2.Controls.Add(this.textBox20);
            this.tabPage2.Controls.Add(this.textBox19);
            this.tabPage2.Controls.Add(this.label27);
            this.tabPage2.Controls.Add(this.checkBox13);
            this.tabPage2.Controls.Add(this.checkBox10);
            this.tabPage2.Controls.Add(this.textBox6);
            this.tabPage2.Controls.Add(this.textBox5);
            this.tabPage2.Controls.Add(this.textBox4);
            this.tabPage2.Controls.Add(this.label22);
            this.tabPage2.Controls.Add(this.label19);
            this.tabPage2.Controls.Add(this.label18);
            this.tabPage2.Controls.Add(this.checkBox6);
            this.tabPage2.Controls.Add(this.checkBox5);
            this.tabPage2.Controls.Add(this.label17);
            this.tabPage2.Controls.Add(this.textBox3);
            this.tabPage2.Controls.Add(this.checkBox2);
            this.tabPage2.Controls.Add(this.button11);
            this.tabPage2.Controls.Add(this.checkBox20);
            this.tabPage2.Controls.Add(this.button8);
            this.tabPage2.Controls.Add(this.checkBox1);
            this.tabPage2.Controls.Add(this.textBox14);
            this.tabPage2.Controls.Add(this.label21);
            this.tabPage2.Controls.Add(this.textBox13);
            this.tabPage2.Controls.Add(this.label20);
            this.tabPage2.Controls.Add(this.label15);
            this.tabPage2.Controls.Add(this.textBox11);
            this.tabPage2.Controls.Add(this.textBox10);
            this.tabPage2.Controls.Add(this.label14);
            this.tabPage2.Controls.Add(this.button2);
            this.tabPage2.Location = new System.Drawing.Point(4, 27);
            this.tabPage2.Name = "tabPage2";
            this.tabPage2.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage2.Size = new System.Drawing.Size(691, 345);
            this.tabPage2.TabIndex = 1;
            this.tabPage2.Text = "Сбор данных";
            this.tabPage2.UseVisualStyleBackColor = true;
            // 
            // button14
            // 
            this.button14.Location = new System.Drawing.Point(366, 254);
            this.button14.Name = "button14";
            this.button14.Size = new System.Drawing.Size(198, 30);
            this.button14.TabIndex = 54;
            this.button14.Text = "Выбрать папку";
            this.button14.UseVisualStyleBackColor = true;
            this.button14.Click += new System.EventHandler(this.button14_Click);
            // 
            // label28
            // 
            this.label28.AutoSize = true;
            this.label28.Location = new System.Drawing.Point(3, 291);
            this.label28.Name = "label28";
            this.label28.Size = new System.Drawing.Size(38, 18);
            this.label28.TabIndex = 53;
            this.label28.Text = "Имя";
            // 
            // textBox20
            // 
            this.textBox20.Location = new System.Drawing.Point(117, 285);
            this.textBox20.Name = "textBox20";
            this.textBox20.Size = new System.Drawing.Size(571, 24);
            this.textBox20.TabIndex = 52;
            this.textBox20.Text = "test_com2.txt";
            // 
            // textBox19
            // 
            this.textBox19.Location = new System.Drawing.Point(542, 204);
            this.textBox19.Name = "textBox19";
            this.textBox19.Size = new System.Drawing.Size(100, 24);
            this.textBox19.TabIndex = 51;
            this.textBox19.Text = "1";
            this.textBox19.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // label27
            // 
            this.label27.AutoSize = true;
            this.label27.Location = new System.Drawing.Point(395, 206);
            this.label27.Name = "label27";
            this.label27.Size = new System.Drawing.Size(141, 18);
            this.label27.TabIndex = 50;
            this.label27.Text = "Сохранять каждый";
            // 
            // checkBox13
            // 
            this.checkBox13.AutoSize = true;
            this.checkBox13.Checked = true;
            this.checkBox13.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBox13.Location = new System.Drawing.Point(452, 77);
            this.checkBox13.Name = "checkBox13";
            this.checkBox13.Size = new System.Drawing.Size(196, 22);
            this.checkBox13.TabIndex = 49;
            this.checkBox13.Text = "Сохранять сырой замер";
            this.checkBox13.UseVisualStyleBackColor = true;
            // 
            // checkBox10
            // 
            this.checkBox10.AutoSize = true;
            this.checkBox10.Checked = true;
            this.checkBox10.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBox10.Location = new System.Drawing.Point(451, 48);
            this.checkBox10.Name = "checkBox10";
            this.checkBox10.Size = new System.Drawing.Size(237, 22);
            this.checkBox10.TabIndex = 48;
            this.checkBox10.Text = "Сохранять файл с временами";
            this.checkBox10.UseVisualStyleBackColor = true;
            // 
            // textBox6
            // 
            this.textBox6.Location = new System.Drawing.Point(260, 251);
            this.textBox6.Name = "textBox6";
            this.textBox6.Size = new System.Drawing.Size(100, 24);
            this.textBox6.TabIndex = 47;
            this.textBox6.Text = "7000";
            // 
            // textBox5
            // 
            this.textBox5.Location = new System.Drawing.Point(260, 224);
            this.textBox5.Name = "textBox5";
            this.textBox5.Size = new System.Drawing.Size(100, 24);
            this.textBox5.TabIndex = 46;
            this.textBox5.Text = "100";
            // 
            // textBox4
            // 
            this.textBox4.Location = new System.Drawing.Point(260, 196);
            this.textBox4.Name = "textBox4";
            this.textBox4.Size = new System.Drawing.Size(100, 24);
            this.textBox4.TabIndex = 45;
            this.textBox4.Text = "100";
            // 
            // label22
            // 
            this.label22.AutoSize = true;
            this.label22.Location = new System.Drawing.Point(8, 254);
            this.label22.Name = "label22";
            this.label22.Size = new System.Drawing.Size(246, 18);
            this.label22.TabIndex = 44;
            this.label22.Text = "Количество шагов по частоте ПФ";
            // 
            // label19
            // 
            this.label19.AutoSize = true;
            this.label19.Location = new System.Drawing.Point(8, 230);
            this.label19.Name = "label19";
            this.label19.Size = new System.Drawing.Size(143, 18);
            this.label19.TabIndex = 43;
            this.label19.Text = "Шаг по частоте ПФ";
            // 
            // label18
            // 
            this.label18.AutoSize = true;
            this.label18.Location = new System.Drawing.Point(8, 204);
            this.label18.Name = "label18";
            this.label18.Size = new System.Drawing.Size(149, 18);
            this.label18.TabIndex = 42;
            this.label18.Text = "Нижняя частота ПФ";
            // 
            // checkBox6
            // 
            this.checkBox6.AutoSize = true;
            this.checkBox6.Location = new System.Drawing.Point(11, 179);
            this.checkBox6.Name = "checkBox6";
            this.checkBox6.Size = new System.Drawing.Size(320, 22);
            this.checkBox6.TabIndex = 41;
            this.checkBox6.Text = "Если считается ПФ, то посчитать модуль";
            this.checkBox6.UseVisualStyleBackColor = true;
            // 
            // checkBox5
            // 
            this.checkBox5.AutoSize = true;
            this.checkBox5.Location = new System.Drawing.Point(11, 155);
            this.checkBox5.Name = "checkBox5";
            this.checkBox5.Size = new System.Drawing.Size(112, 22);
            this.checkBox5.TabIndex = 40;
            this.checkBox5.Text = "Считать ПФ";
            this.checkBox5.UseVisualStyleBackColor = true;
            // 
            // label17
            // 
            this.label17.AutoSize = true;
            this.label17.Location = new System.Drawing.Point(3, 321);
            this.label17.Name = "label17";
            this.label17.Size = new System.Drawing.Size(108, 18);
            this.label17.TabIndex = 39;
            this.label17.Text = "Путь хранения";
            this.label17.Click += new System.EventHandler(this.label17_Click);
            // 
            // textBox3
            // 
            this.textBox3.Location = new System.Drawing.Point(117, 315);
            this.textBox3.Name = "textBox3";
            this.textBox3.Size = new System.Drawing.Size(571, 24);
            this.textBox3.TabIndex = 38;
            this.textBox3.Text = "C:\\testhead10\\";
            this.textBox3.TextChanged += new System.EventHandler(this.textBox3_TextChanged);
            // 
            // checkBox2
            // 
            this.checkBox2.AutoSize = true;
            this.checkBox2.Location = new System.Drawing.Point(451, 20);
            this.checkBox2.Name = "checkBox2";
            this.checkBox2.Size = new System.Drawing.Size(226, 22);
            this.checkBox2.TabIndex = 37;
            this.checkBox2.Text = "Подавлять начальную часть";
            this.checkBox2.UseVisualStyleBackColor = true;
            // 
            // button11
            // 
            this.button11.Location = new System.Drawing.Point(7, 6);
            this.button11.Name = "button11";
            this.button11.Size = new System.Drawing.Size(182, 35);
            this.button11.TabIndex = 36;
            this.button11.Text = "Автоматический сбор данных";
            this.button11.UseVisualStyleBackColor = true;
            this.button11.Click += new System.EventHandler(this.button11_Click);
            // 
            // checkBox20
            // 
            this.checkBox20.AutoSize = true;
            this.checkBox20.Location = new System.Drawing.Point(11, 131);
            this.checkBox20.Name = "checkBox20";
            this.checkBox20.Size = new System.Drawing.Size(125, 22);
            this.checkBox20.TabIndex = 35;
            this.checkBox20.Text = "Визуализация";
            this.checkBox20.UseVisualStyleBackColor = true;
            // 
            // button8
            // 
            this.button8.Location = new System.Drawing.Point(7, 48);
            this.button8.Name = "button8";
            this.button8.Size = new System.Drawing.Size(182, 32);
            this.button8.TabIndex = 34;
            this.button8.Text = "Стоп";
            this.button8.UseVisualStyleBackColor = true;
            this.button8.Click += new System.EventHandler(this.button8_Click);
            // 
            // checkBox1
            // 
            this.checkBox1.AutoSize = true;
            this.checkBox1.Checked = true;
            this.checkBox1.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBox1.Location = new System.Drawing.Point(228, 154);
            this.checkBox1.Name = "checkBox1";
            this.checkBox1.Size = new System.Drawing.Size(321, 22);
            this.checkBox1.TabIndex = 33;
            this.checkBox1.Text = "Ограничение полосы пропускания 20 МГц";
            this.checkBox1.UseVisualStyleBackColor = true;
            // 
            // textBox14
            // 
            this.textBox14.Location = new System.Drawing.Point(345, 21);
            this.textBox14.Name = "textBox14";
            this.textBox14.Size = new System.Drawing.Size(100, 24);
            this.textBox14.TabIndex = 32;
            this.textBox14.Text = "6300";
            this.textBox14.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // label21
            // 
            this.label21.AutoSize = true;
            this.label21.Location = new System.Drawing.Point(195, 24);
            this.label21.Name = "label21";
            this.label21.Size = new System.Drawing.Size(156, 18);
            this.label21.TabIndex = 31;
            this.label21.Text = "Число подавляемых ";
            // 
            // textBox13
            // 
            this.textBox13.Location = new System.Drawing.Point(345, 56);
            this.textBox13.Name = "textBox13";
            this.textBox13.Size = new System.Drawing.Size(100, 24);
            this.textBox13.TabIndex = 30;
            this.textBox13.Text = "5000";
            this.textBox13.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // label20
            // 
            this.label20.AutoSize = true;
            this.label20.Location = new System.Drawing.Point(195, 59);
            this.label20.Name = "label20";
            this.label20.Size = new System.Drawing.Size(120, 18);
            this.label20.TabIndex = 29;
            this.label20.Text = "Число шагов до";
            // 
            // label15
            // 
            this.label15.AutoSize = true;
            this.label15.Location = new System.Drawing.Point(195, 132);
            this.label15.Name = "label15";
            this.label15.Size = new System.Drawing.Size(136, 18);
            this.label15.TabIndex = 28;
            this.label15.Text = "Число усреднений";
            // 
            // textBox11
            // 
            this.textBox11.Location = new System.Drawing.Point(345, 128);
            this.textBox11.Name = "textBox11";
            this.textBox11.Size = new System.Drawing.Size(100, 24);
            this.textBox11.TabIndex = 27;
            this.textBox11.Text = "500";
            this.textBox11.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // textBox10
            // 
            this.textBox10.Location = new System.Drawing.Point(345, 93);
            this.textBox10.Name = "textBox10";
            this.textBox10.Size = new System.Drawing.Size(100, 24);
            this.textBox10.TabIndex = 26;
            this.textBox10.Text = "95000";
            this.textBox10.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // label14
            // 
            this.label14.AutoSize = true;
            this.label14.Location = new System.Drawing.Point(195, 99);
            this.label14.Name = "label14";
            this.label14.Size = new System.Drawing.Size(144, 18);
            this.label14.TabIndex = 25;
            this.label14.Text = "Число шагов после";
            // 
            // button2
            // 
            this.button2.Location = new System.Drawing.Point(585, 254);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(103, 30);
            this.button2.TabIndex = 0;
            this.button2.Text = "Сбор данных";
            this.button2.UseVisualStyleBackColor = true;
            this.button2.Click += new System.EventHandler(this.button2_Click);
            // 
            // tabPage4
            // 
            this.tabPage4.Controls.Add(this.textBox16);
            this.tabPage4.Controls.Add(this.checkBox9);
            this.tabPage4.Controls.Add(this.checkBox8);
            this.tabPage4.Controls.Add(this.checkBox4);
            this.tabPage4.Controls.Add(this.checkBox3);
            this.tabPage4.Controls.Add(this.button10);
            this.tabPage4.Controls.Add(this.textBox2);
            this.tabPage4.Controls.Add(this.button9);
            this.tabPage4.Location = new System.Drawing.Point(4, 27);
            this.tabPage4.Name = "tabPage4";
            this.tabPage4.Size = new System.Drawing.Size(691, 345);
            this.tabPage4.TabIndex = 3;
            this.tabPage4.Text = "Предобработка";
            this.tabPage4.UseVisualStyleBackColor = true;
            // 
            // textBox16
            // 
            this.textBox16.Location = new System.Drawing.Point(6, 185);
            this.textBox16.Name = "textBox16";
            this.textBox16.Size = new System.Drawing.Size(646, 24);
            this.textBox16.TabIndex = 8;
            this.textBox16.Text = "C:\\testhead10\\my_filter_f200.txt";
            // 
            // checkBox9
            // 
            this.checkBox9.AutoSize = true;
            this.checkBox9.Location = new System.Drawing.Point(4, 161);
            this.checkBox9.Name = "checkBox9";
            this.checkBox9.Size = new System.Drawing.Size(335, 22);
            this.checkBox9.TabIndex = 7;
            this.checkBox9.Text = "Использовать фильтр в частотной области";
            this.checkBox9.UseVisualStyleBackColor = true;
            // 
            // checkBox8
            // 
            this.checkBox8.AutoSize = true;
            this.checkBox8.Location = new System.Drawing.Point(6, 111);
            this.checkBox8.Name = "checkBox8";
            this.checkBox8.Size = new System.Drawing.Size(339, 22);
            this.checkBox8.TabIndex = 5;
            this.checkBox8.Text = "Использовать фильтр в временной области";
            this.checkBox8.UseVisualStyleBackColor = true;
            // 
            // checkBox4
            // 
            this.checkBox4.AutoSize = true;
            this.checkBox4.Checked = true;
            this.checkBox4.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBox4.Location = new System.Drawing.Point(143, 72);
            this.checkBox4.Name = "checkBox4";
            this.checkBox4.Size = new System.Drawing.Size(570, 22);
            this.checkBox4.TabIndex = 4;
            this.checkBox4.Text = "Применять устранение средней величины при автоматическом сборе данных";
            this.checkBox4.UseVisualStyleBackColor = true;
            // 
            // checkBox3
            // 
            this.checkBox3.AutoSize = true;
            this.checkBox3.Checked = true;
            this.checkBox3.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBox3.Location = new System.Drawing.Point(143, 44);
            this.checkBox3.Name = "checkBox3";
            this.checkBox3.Size = new System.Drawing.Size(477, 22);
            this.checkBox3.TabIndex = 3;
            this.checkBox3.Text = "Применять бегущее среднее при автоматическом сборе данных";
            this.checkBox3.UseVisualStyleBackColor = true;
            // 
            // button10
            // 
            this.button10.Location = new System.Drawing.Point(5, 57);
            this.button10.Name = "button10";
            this.button10.Size = new System.Drawing.Size(132, 37);
            this.button10.TabIndex = 2;
            this.button10.Text = "Устранить среднюю величину";
            this.button10.UseVisualStyleBackColor = true;
            this.button10.Click += new System.EventHandler(this.button10_Click);
            // 
            // textBox2
            // 
            this.textBox2.Location = new System.Drawing.Point(143, 14);
            this.textBox2.Name = "textBox2";
            this.textBox2.Size = new System.Drawing.Size(100, 24);
            this.textBox2.TabIndex = 1;
            this.textBox2.Text = "5";
            // 
            // button9
            // 
            this.button9.Location = new System.Drawing.Point(5, 14);
            this.button9.Name = "button9";
            this.button9.Size = new System.Drawing.Size(132, 37);
            this.button9.TabIndex = 0;
            this.button9.Text = "Применить бегущее среднее";
            this.button9.UseVisualStyleBackColor = true;
            this.button9.Click += new System.EventHandler(this.button9_Click);
            // 
            // tabPage5
            // 
            this.tabPage5.Controls.Add(this.checkBox12);
            this.tabPage5.Controls.Add(this.textBox18);
            this.tabPage5.Controls.Add(this.checkBox11);
            this.tabPage5.Controls.Add(this.textBox17);
            this.tabPage5.Controls.Add(this.textBox12);
            this.tabPage5.Controls.Add(this.label25);
            this.tabPage5.Controls.Add(this.checkBox7);
            this.tabPage5.Controls.Add(this.button12);
            this.tabPage5.Controls.Add(this.textBox8);
            this.tabPage5.Controls.Add(this.label24);
            this.tabPage5.Controls.Add(this.label23);
            this.tabPage5.Controls.Add(this.textBox7);
            this.tabPage5.Location = new System.Drawing.Point(4, 27);
            this.tabPage5.Name = "tabPage5";
            this.tabPage5.Size = new System.Drawing.Size(691, 345);
            this.tabPage5.TabIndex = 4;
            this.tabPage5.Text = "Обработка";
            this.tabPage5.UseVisualStyleBackColor = true;
            // 
            // checkBox12
            // 
            this.checkBox12.AutoSize = true;
            this.checkBox12.Location = new System.Drawing.Point(376, 143);
            this.checkBox12.Name = "checkBox12";
            this.checkBox12.Size = new System.Drawing.Size(241, 22);
            this.checkBox12.TabIndex = 11;
            this.checkBox12.Text = "Применить фильтр по частоте";
            this.checkBox12.UseVisualStyleBackColor = true;
            // 
            // textBox18
            // 
            this.textBox18.Location = new System.Drawing.Point(531, 103);
            this.textBox18.Name = "textBox18";
            this.textBox18.Size = new System.Drawing.Size(151, 24);
            this.textBox18.TabIndex = 10;
            this.textBox18.Text = "5100";
            // 
            // checkBox11
            // 
            this.checkBox11.AutoSize = true;
            this.checkBox11.Location = new System.Drawing.Point(242, 103);
            this.checkBox11.Name = "checkBox11";
            this.checkBox11.Size = new System.Drawing.Size(126, 22);
            this.checkBox11.TabIndex = 9;
            this.checkBox11.Text = "Подавлять до";
            this.checkBox11.UseVisualStyleBackColor = true;
            // 
            // textBox17
            // 
            this.textBox17.Location = new System.Drawing.Point(8, 172);
            this.textBox17.Multiline = true;
            this.textBox17.Name = "textBox17";
            this.textBox17.Size = new System.Drawing.Size(674, 170);
            this.textBox17.TabIndex = 8;
            this.textBox17.Text = "Информация о построенном разностном замере";
            // 
            // textBox12
            // 
            this.textBox12.Location = new System.Drawing.Point(242, 66);
            this.textBox12.Name = "textBox12";
            this.textBox12.Size = new System.Drawing.Size(440, 24);
            this.textBox12.TabIndex = 7;
            this.textBox12.Text = "C:\\Users\\user1\\Desktop\\ext_new\\16mm\\razn\\";
            // 
            // label25
            // 
            this.label25.AutoSize = true;
            this.label25.Location = new System.Drawing.Point(5, 72);
            this.label25.Name = "label25";
            this.label25.Size = new System.Drawing.Size(241, 18);
            this.label25.TabIndex = 6;
            this.label25.Text = "Папка для сохранения разностей";
            // 
            // checkBox7
            // 
            this.checkBox7.AutoSize = true;
            this.checkBox7.Location = new System.Drawing.Point(8, 143);
            this.checkBox7.Name = "checkBox7";
            this.checkBox7.Size = new System.Drawing.Size(326, 22);
            this.checkBox7.TabIndex = 5;
            this.checkBox7.Text = "Нормализовывать перед выводом в файл";
            this.checkBox7.UseVisualStyleBackColor = true;
            // 
            // button12
            // 
            this.button12.Location = new System.Drawing.Point(8, 99);
            this.button12.Name = "button12";
            this.button12.Size = new System.Drawing.Size(167, 28);
            this.button12.TabIndex = 4;
            this.button12.Text = "Построить разности";
            this.button12.UseVisualStyleBackColor = true;
            this.button12.Click += new System.EventHandler(this.button12_Click);
            // 
            // textBox8
            // 
            this.textBox8.Location = new System.Drawing.Point(242, 40);
            this.textBox8.Name = "textBox8";
            this.textBox8.Size = new System.Drawing.Size(440, 24);
            this.textBox8.TabIndex = 3;
            this.textBox8.Text = "C:\\Users\\user1\\Desktop\\ext_new\\16mm\\def\\";
            // 
            // label24
            // 
            this.label24.AutoSize = true;
            this.label24.Location = new System.Drawing.Point(5, 46);
            this.label24.Name = "label24";
            this.label24.Size = new System.Drawing.Size(226, 18);
            this.label24.TabIndex = 2;
            this.label24.Text = "Папка с замерами с дефектом";
            // 
            // label23
            // 
            this.label23.AutoSize = true;
            this.label23.Location = new System.Drawing.Point(5, 21);
            this.label23.Name = "label23";
            this.label23.Size = new System.Drawing.Size(231, 18);
            this.label23.TabIndex = 1;
            this.label23.Text = "Папка с замерами без дефекта";
            // 
            // textBox7
            // 
            this.textBox7.Location = new System.Drawing.Point(242, 15);
            this.textBox7.Name = "textBox7";
            this.textBox7.Size = new System.Drawing.Size(440, 24);
            this.textBox7.TabIndex = 0;
            this.textBox7.Text = "C:\\Users\\user1\\Desktop\\ext_new\\16mm\\bez\\";
            // 
            // tabPage6
            // 
            this.tabPage6.Controls.Add(this.pictureBox1);
            this.tabPage6.Location = new System.Drawing.Point(4, 27);
            this.tabPage6.Name = "tabPage6";
            this.tabPage6.Size = new System.Drawing.Size(691, 345);
            this.tabPage6.TabIndex = 5;
            this.tabPage6.Text = "Визуализация";
            this.tabPage6.UseVisualStyleBackColor = true;
            this.tabPage6.Click += new System.EventHandler(this.tabPage6_Click);
            // 
            // pictureBox1
            // 
            this.pictureBox1.Location = new System.Drawing.Point(0, 0);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(688, 342);
            this.pictureBox1.TabIndex = 0;
            this.pictureBox1.TabStop = false;
            // 
            // tabPage7
            // 
            this.tabPage7.Controls.Add(this.chart1);
            this.tabPage7.Location = new System.Drawing.Point(4, 27);
            this.tabPage7.Name = "tabPage7";
            this.tabPage7.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage7.Size = new System.Drawing.Size(691, 345);
            this.tabPage7.TabIndex = 6;
            this.tabPage7.Text = "tabPage7";
            this.tabPage7.UseVisualStyleBackColor = true;
            // 
            // chart1
            // 
            chartArea1.Name = "ChartArea1";
            this.chart1.ChartAreas.Add(chartArea1);
            legend1.Name = "Legend1";
            this.chart1.Legends.Add(legend1);
            this.chart1.Location = new System.Drawing.Point(0, 3);
            this.chart1.Name = "chart1";
            series1.ChartArea = "ChartArea1";
            series1.ChartType = System.Windows.Forms.DataVisualization.Charting.SeriesChartType.FastLine;
            series1.Legend = "Legend1";
            series1.Name = "Series1";
            this.chart1.Series.Add(series1);
            this.chart1.Size = new System.Drawing.Size(688, 342);
            this.chart1.TabIndex = 2;
            this.chart1.Text = "chart1";
            // 
            // tabPage8
            // 
            this.tabPage8.Controls.Add(this.button15);
            this.tabPage8.Location = new System.Drawing.Point(4, 27);
            this.tabPage8.Name = "tabPage8";
            this.tabPage8.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage8.Size = new System.Drawing.Size(691, 345);
            this.tabPage8.TabIndex = 7;
            this.tabPage8.Text = "TEstinGGGG";
            this.tabPage8.UseVisualStyleBackColor = true;
            // 
            // button15
            // 
            this.button15.Location = new System.Drawing.Point(18, 22);
            this.button15.Name = "button15";
            this.button15.Size = new System.Drawing.Size(75, 23);
            this.button15.TabIndex = 0;
            this.button15.Text = "button15";
            this.button15.UseVisualStyleBackColor = true;
            this.button15.Click += new System.EventHandler(this.button15_Click);
            // 
            // tabPage9
            // 
            this.tabPage9.Controls.Add(this.checkBox14);
            this.tabPage9.Controls.Add(this.button17);
            this.tabPage9.Controls.Add(this.label29);
            this.tabPage9.Controls.Add(this.textBox22);
            this.tabPage9.Location = new System.Drawing.Point(4, 27);
            this.tabPage9.Name = "tabPage9";
            this.tabPage9.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage9.Size = new System.Drawing.Size(691, 345);
            this.tabPage9.TabIndex = 8;
            this.tabPage9.Text = "Фурье";
            this.tabPage9.UseVisualStyleBackColor = true;
            // 
            // checkBox14
            // 
            this.checkBox14.AutoSize = true;
            this.checkBox14.Location = new System.Drawing.Point(11, 98);
            this.checkBox14.Name = "checkBox14";
            this.checkBox14.Size = new System.Drawing.Size(326, 22);
            this.checkBox14.TabIndex = 9;
            this.checkBox14.Text = "Нормализовывать перед выводом в файл";
            this.checkBox14.UseVisualStyleBackColor = true;
            // 
            // button17
            // 
            this.button17.Location = new System.Drawing.Point(11, 64);
            this.button17.Name = "button17";
            this.button17.Size = new System.Drawing.Size(167, 28);
            this.button17.TabIndex = 8;
            this.button17.Text = "Посчитать Фурье";
            this.button17.UseVisualStyleBackColor = true;
            this.button17.Click += new System.EventHandler(this.button17_Click);
            // 
            // label29
            // 
            this.label29.AutoSize = true;
            this.label29.Location = new System.Drawing.Point(8, 13);
            this.label29.Name = "label29";
            this.label29.Size = new System.Drawing.Size(137, 18);
            this.label29.TabIndex = 7;
            this.label29.Text = "Папка с замерами";
            // 
            // textBox22
            // 
            this.textBox22.Location = new System.Drawing.Point(11, 34);
            this.textBox22.Name = "textBox22";
            this.textBox22.Size = new System.Drawing.Size(677, 24);
            this.textBox22.TabIndex = 6;
            this.textBox22.Text = "C:\\Users\\user1\\Desktop\\ext_new\\16mm\\bez\\";
            // 
            // tabPage10
            // 
            this.tabPage10.Controls.Add(this.button25);
            this.tabPage10.Controls.Add(this.button24);
            this.tabPage10.Controls.Add(this.button23);
            this.tabPage10.Controls.Add(this.button22);
            this.tabPage10.Controls.Add(this.textBox28);
            this.tabPage10.Controls.Add(this.label35);
            this.tabPage10.Controls.Add(this.textBox29);
            this.tabPage10.Controls.Add(this.label36);
            this.tabPage10.Controls.Add(this.label34);
            this.tabPage10.Controls.Add(this.textBox27);
            this.tabPage10.Controls.Add(this.textBox26);
            this.tabPage10.Controls.Add(this.button21);
            this.tabPage10.Controls.Add(this.listBox2);
            this.tabPage10.Controls.Add(this.button19);
            this.tabPage10.Controls.Add(this.button20);
            this.tabPage10.Controls.Add(this.textBox25);
            this.tabPage10.Controls.Add(this.label33);
            this.tabPage10.Controls.Add(this.textBox24);
            this.tabPage10.Controls.Add(this.label32);
            this.tabPage10.Controls.Add(this.textBox23);
            this.tabPage10.Controls.Add(this.label31);
            this.tabPage10.Controls.Add(this.button18);
            this.tabPage10.Controls.Add(this.textBox15);
            this.tabPage10.Location = new System.Drawing.Point(4, 27);
            this.tabPage10.Name = "tabPage10";
            this.tabPage10.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage10.Size = new System.Drawing.Size(691, 345);
            this.tabPage10.TabIndex = 9;
            this.tabPage10.Text = "gcode test";
            this.tabPage10.UseVisualStyleBackColor = true;
            // 
            // button25
            // 
            this.button25.Location = new System.Drawing.Point(385, 197);
            this.button25.Name = "button25";
            this.button25.Size = new System.Drawing.Size(75, 23);
            this.button25.TabIndex = 50;
            this.button25.Text = "RIGHT";
            this.button25.UseVisualStyleBackColor = true;
            this.button25.Click += new System.EventHandler(this.button25_Click);
            // 
            // button24
            // 
            this.button24.Location = new System.Drawing.Point(222, 197);
            this.button24.Name = "button24";
            this.button24.Size = new System.Drawing.Size(75, 23);
            this.button24.TabIndex = 49;
            this.button24.Text = "LEFT";
            this.button24.UseVisualStyleBackColor = true;
            this.button24.Click += new System.EventHandler(this.button24_Click);
            // 
            // button23
            // 
            this.button23.Location = new System.Drawing.Point(303, 226);
            this.button23.Name = "button23";
            this.button23.Size = new System.Drawing.Size(75, 23);
            this.button23.TabIndex = 48;
            this.button23.Text = "DOWN";
            this.button23.UseVisualStyleBackColor = true;
            this.button23.Click += new System.EventHandler(this.button23_Click);
            // 
            // button22
            // 
            this.button22.Location = new System.Drawing.Point(303, 168);
            this.button22.Name = "button22";
            this.button22.Size = new System.Drawing.Size(75, 23);
            this.button22.TabIndex = 47;
            this.button22.Text = "UP";
            this.button22.UseVisualStyleBackColor = true;
            this.button22.Click += new System.EventHandler(this.button22_Click);
            // 
            // textBox28
            // 
            this.textBox28.Location = new System.Drawing.Point(63, 291);
            this.textBox28.Name = "textBox28";
            this.textBox28.Size = new System.Drawing.Size(100, 24);
            this.textBox28.TabIndex = 46;
            this.textBox28.Text = "10";
            // 
            // label35
            // 
            this.label35.AutoSize = true;
            this.label35.Location = new System.Drawing.Point(34, 297);
            this.label35.Name = "label35";
            this.label35.Size = new System.Drawing.Size(23, 18);
            this.label35.TabIndex = 45;
            this.label35.Text = "dy";
            // 
            // textBox29
            // 
            this.textBox29.Location = new System.Drawing.Point(63, 261);
            this.textBox29.Name = "textBox29";
            this.textBox29.Size = new System.Drawing.Size(100, 24);
            this.textBox29.TabIndex = 44;
            this.textBox29.Text = "10";
            // 
            // label36
            // 
            this.label36.AutoSize = true;
            this.label36.Location = new System.Drawing.Point(34, 267);
            this.label36.Name = "label36";
            this.label36.Size = new System.Drawing.Size(23, 18);
            this.label36.TabIndex = 43;
            this.label36.Text = "dx";
            // 
            // label34
            // 
            this.label34.AutoSize = true;
            this.label34.Location = new System.Drawing.Point(182, 145);
            this.label34.Name = "label34";
            this.label34.Size = new System.Drawing.Size(42, 18);
            this.label34.TabIndex = 42;
            this.label34.Text = "px py";
            // 
            // textBox27
            // 
            this.textBox27.Location = new System.Drawing.Point(482, 121);
            this.textBox27.Multiline = true;
            this.textBox27.Name = "textBox27";
            this.textBox27.Size = new System.Drawing.Size(201, 167);
            this.textBox27.TabIndex = 41;
            // 
            // textBox26
            // 
            this.textBox26.Location = new System.Drawing.Point(25, 168);
            this.textBox26.Name = "textBox26";
            this.textBox26.Size = new System.Drawing.Size(91, 24);
            this.textBox26.TabIndex = 40;
            this.textBox26.Text = "g0 x10 y10";
            // 
            // button21
            // 
            this.button21.Location = new System.Drawing.Point(25, 212);
            this.button21.Name = "button21";
            this.button21.Size = new System.Drawing.Size(75, 37);
            this.button21.TabIndex = 39;
            this.button21.Text = "SEND";
            this.button21.UseVisualStyleBackColor = true;
            this.button21.Click += new System.EventHandler(this.button21_Click);
            // 
            // listBox2
            // 
            this.listBox2.FormattingEnabled = true;
            this.listBox2.ItemHeight = 18;
            this.listBox2.Location = new System.Drawing.Point(394, 23);
            this.listBox2.Name = "listBox2";
            this.listBox2.Size = new System.Drawing.Size(82, 40);
            this.listBox2.TabIndex = 38;
            this.listBox2.SelectedIndexChanged += new System.EventHandler(this.listBox2_SelectedIndexChanged);
            // 
            // button19
            // 
            this.button19.Location = new System.Drawing.Point(264, 53);
            this.button19.Name = "button19";
            this.button19.Size = new System.Drawing.Size(124, 28);
            this.button19.TabIndex = 37;
            this.button19.Text = "Подключить порт";
            this.button19.UseVisualStyleBackColor = true;
            this.button19.Click += new System.EventHandler(this.button19_Click);
            // 
            // button20
            // 
            this.button20.Location = new System.Drawing.Point(264, 23);
            this.button20.Name = "button20";
            this.button20.Size = new System.Drawing.Size(124, 29);
            this.button20.TabIndex = 36;
            this.button20.Text = "Список портов";
            this.button20.UseVisualStyleBackColor = true;
            this.button20.Click += new System.EventHandler(this.button20_Click);
            // 
            // textBox25
            // 
            this.textBox25.Location = new System.Drawing.Point(109, 80);
            this.textBox25.Name = "textBox25";
            this.textBox25.Size = new System.Drawing.Size(100, 24);
            this.textBox25.TabIndex = 6;
            this.textBox25.Text = "y0";
            // 
            // label33
            // 
            this.label33.AutoSize = true;
            this.label33.Location = new System.Drawing.Point(22, 83);
            this.label33.Name = "label33";
            this.label33.Size = new System.Drawing.Size(15, 18);
            this.label33.TabIndex = 5;
            this.label33.Text = "y";
            // 
            // textBox24
            // 
            this.textBox24.Location = new System.Drawing.Point(109, 50);
            this.textBox24.Name = "textBox24";
            this.textBox24.Size = new System.Drawing.Size(100, 24);
            this.textBox24.TabIndex = 4;
            this.textBox24.Text = "x0";
            // 
            // label32
            // 
            this.label32.AutoSize = true;
            this.label32.Location = new System.Drawing.Point(22, 53);
            this.label32.Name = "label32";
            this.label32.Size = new System.Drawing.Size(15, 18);
            this.label32.TabIndex = 3;
            this.label32.Text = "x";
            // 
            // textBox23
            // 
            this.textBox23.Location = new System.Drawing.Point(109, 20);
            this.textBox23.Name = "textBox23";
            this.textBox23.Size = new System.Drawing.Size(100, 24);
            this.textBox23.TabIndex = 2;
            this.textBox23.Text = "g0";
            // 
            // label31
            // 
            this.label31.AutoSize = true;
            this.label31.Location = new System.Drawing.Point(22, 23);
            this.label31.Name = "label31";
            this.label31.Size = new System.Drawing.Size(46, 18);
            this.label31.TabIndex = 1;
            this.label31.Text = "mode";
            // 
            // button18
            // 
            this.button18.Location = new System.Drawing.Point(25, 115);
            this.button18.Name = "button18";
            this.button18.Size = new System.Drawing.Size(75, 37);
            this.button18.TabIndex = 0;
            this.button18.Text = "SEND";
            this.button18.UseVisualStyleBackColor = true;
            this.button18.Click += new System.EventHandler(this.button18_Click);
            // 
            // textBox15
            // 
            this.textBox15.Location = new System.Drawing.Point(264, 104);
            this.textBox15.Name = "textBox15";
            this.textBox15.Size = new System.Drawing.Size(99, 24);
            this.textBox15.TabIndex = 1;
            this.textBox15.Text = "115200";
            // 
            // tabPage11
            // 
            this.tabPage11.Controls.Add(this.checkBox17);
            this.tabPage11.Controls.Add(this.checkBox16);
            this.tabPage11.Controls.Add(this.textBox38);
            this.tabPage11.Controls.Add(this.label45);
            this.tabPage11.Controls.Add(this.textBox36);
            this.tabPage11.Controls.Add(this.label43);
            this.tabPage11.Controls.Add(this.textBox37);
            this.tabPage11.Controls.Add(this.label44);
            this.tabPage11.Controls.Add(this.checkBox15);
            this.tabPage11.Controls.Add(this.button26);
            this.tabPage11.Controls.Add(this.textBox34);
            this.tabPage11.Controls.Add(this.label41);
            this.tabPage11.Controls.Add(this.textBox35);
            this.tabPage11.Controls.Add(this.label42);
            this.tabPage11.Controls.Add(this.textBox32);
            this.tabPage11.Controls.Add(this.label39);
            this.tabPage11.Controls.Add(this.textBox33);
            this.tabPage11.Controls.Add(this.label40);
            this.tabPage11.Controls.Add(this.textBox30);
            this.tabPage11.Controls.Add(this.label37);
            this.tabPage11.Controls.Add(this.textBox31);
            this.tabPage11.Controls.Add(this.label38);
            this.tabPage11.Location = new System.Drawing.Point(4, 27);
            this.tabPage11.Name = "tabPage11";
            this.tabPage11.Size = new System.Drawing.Size(691, 345);
            this.tabPage11.TabIndex = 10;
            this.tabPage11.Text = "ScamAcoustic";
            this.tabPage11.UseVisualStyleBackColor = true;
            // 
            // checkBox17
            // 
            this.checkBox17.AutoSize = true;
            this.checkBox17.Location = new System.Drawing.Point(178, 135);
            this.checkBox17.Name = "checkBox17";
            this.checkBox17.Size = new System.Drawing.Size(118, 22);
            this.checkBox17.TabIndex = 28;
            this.checkBox17.Text = "Move Along Y";
            this.checkBox17.UseVisualStyleBackColor = true;
            // 
            // checkBox16
            // 
            this.checkBox16.AutoSize = true;
            this.checkBox16.Checked = true;
            this.checkBox16.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBox16.Location = new System.Drawing.Point(178, 107);
            this.checkBox16.Name = "checkBox16";
            this.checkBox16.Size = new System.Drawing.Size(119, 22);
            this.checkBox16.TabIndex = 27;
            this.checkBox16.Text = "Move Along X";
            this.checkBox16.UseVisualStyleBackColor = true;
            // 
            // textBox38
            // 
            this.textBox38.Location = new System.Drawing.Point(166, 26);
            this.textBox38.Name = "textBox38";
            this.textBox38.Size = new System.Drawing.Size(52, 24);
            this.textBox38.TabIndex = 26;
            this.textBox38.Text = "200";
            // 
            // label45
            // 
            this.label45.AutoSize = true;
            this.label45.Location = new System.Drawing.Point(148, 32);
            this.label45.Name = "label45";
            this.label45.Size = new System.Drawing.Size(12, 18);
            this.label45.TabIndex = 25;
            this.label45.Text = "f";
            // 
            // textBox36
            // 
            this.textBox36.Location = new System.Drawing.Point(69, 265);
            this.textBox36.Name = "textBox36";
            this.textBox36.Size = new System.Drawing.Size(52, 24);
            this.textBox36.TabIndex = 24;
            this.textBox36.Text = "0";
            // 
            // label43
            // 
            this.label43.AutoSize = true;
            this.label43.Location = new System.Drawing.Point(23, 268);
            this.label43.Name = "label43";
            this.label43.Size = new System.Drawing.Size(40, 18);
            this.label43.TabIndex = 23;
            this.label43.Text = "off_y";
            // 
            // textBox37
            // 
            this.textBox37.Location = new System.Drawing.Point(69, 232);
            this.textBox37.Name = "textBox37";
            this.textBox37.Size = new System.Drawing.Size(52, 24);
            this.textBox37.TabIndex = 22;
            this.textBox37.Text = "0";
            // 
            // label44
            // 
            this.label44.AutoSize = true;
            this.label44.Location = new System.Drawing.Point(23, 238);
            this.label44.Name = "label44";
            this.label44.Size = new System.Drawing.Size(40, 18);
            this.label44.TabIndex = 21;
            this.label44.Text = "off_x";
            // 
            // checkBox15
            // 
            this.checkBox15.AutoSize = true;
            this.checkBox15.Checked = true;
            this.checkBox15.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBox15.Location = new System.Drawing.Point(435, 40);
            this.checkBox15.Name = "checkBox15";
            this.checkBox15.Size = new System.Drawing.Size(183, 22);
            this.checkBox15.TabIndex = 20;
            this.checkBox15.Text = "Измерять и сохранять";
            this.checkBox15.UseVisualStyleBackColor = true;
            // 
            // button26
            // 
            this.button26.Location = new System.Drawing.Point(275, 32);
            this.button26.Name = "button26";
            this.button26.Size = new System.Drawing.Size(122, 37);
            this.button26.TabIndex = 19;
            this.button26.Text = "Сбор данных";
            this.button26.UseVisualStyleBackColor = true;
            this.button26.Click += new System.EventHandler(this.button26_Click);
            // 
            // textBox34
            // 
            this.textBox34.Location = new System.Drawing.Point(52, 185);
            this.textBox34.Name = "textBox34";
            this.textBox34.Size = new System.Drawing.Size(52, 24);
            this.textBox34.TabIndex = 18;
            this.textBox34.Text = "1";
            // 
            // label41
            // 
            this.label41.AutoSize = true;
            this.label41.Location = new System.Drawing.Point(23, 188);
            this.label41.Name = "label41";
            this.label41.Size = new System.Drawing.Size(23, 18);
            this.label41.TabIndex = 17;
            this.label41.Text = "ny";
            // 
            // textBox35
            // 
            this.textBox35.Location = new System.Drawing.Point(52, 152);
            this.textBox35.Name = "textBox35";
            this.textBox35.Size = new System.Drawing.Size(52, 24);
            this.textBox35.TabIndex = 16;
            this.textBox35.Text = "200";
            // 
            // label42
            // 
            this.label42.AutoSize = true;
            this.label42.Location = new System.Drawing.Point(23, 158);
            this.label42.Name = "label42";
            this.label42.Size = new System.Drawing.Size(23, 18);
            this.label42.TabIndex = 15;
            this.label42.Text = "nx";
            // 
            // textBox32
            // 
            this.textBox32.Location = new System.Drawing.Point(52, 122);
            this.textBox32.Name = "textBox32";
            this.textBox32.Size = new System.Drawing.Size(52, 24);
            this.textBox32.TabIndex = 14;
            this.textBox32.Text = "0";
            // 
            // label39
            // 
            this.label39.AutoSize = true;
            this.label39.Location = new System.Drawing.Point(23, 125);
            this.label39.Name = "label39";
            this.label39.Size = new System.Drawing.Size(23, 18);
            this.label39.TabIndex = 13;
            this.label39.Text = "y1";
            // 
            // textBox33
            // 
            this.textBox33.Location = new System.Drawing.Point(52, 89);
            this.textBox33.Name = "textBox33";
            this.textBox33.Size = new System.Drawing.Size(52, 24);
            this.textBox33.TabIndex = 12;
            this.textBox33.Text = "100";
            // 
            // label40
            // 
            this.label40.AutoSize = true;
            this.label40.Location = new System.Drawing.Point(23, 95);
            this.label40.Name = "label40";
            this.label40.Size = new System.Drawing.Size(23, 18);
            this.label40.TabIndex = 11;
            this.label40.Text = "x1";
            // 
            // textBox30
            // 
            this.textBox30.Location = new System.Drawing.Point(52, 59);
            this.textBox30.Name = "textBox30";
            this.textBox30.Size = new System.Drawing.Size(52, 24);
            this.textBox30.TabIndex = 10;
            this.textBox30.Text = "0";
            // 
            // label37
            // 
            this.label37.AutoSize = true;
            this.label37.Location = new System.Drawing.Point(23, 62);
            this.label37.Name = "label37";
            this.label37.Size = new System.Drawing.Size(23, 18);
            this.label37.TabIndex = 9;
            this.label37.Text = "y0";
            // 
            // textBox31
            // 
            this.textBox31.Location = new System.Drawing.Point(52, 26);
            this.textBox31.Name = "textBox31";
            this.textBox31.Size = new System.Drawing.Size(52, 24);
            this.textBox31.TabIndex = 8;
            this.textBox31.Text = "0";
            // 
            // label38
            // 
            this.label38.AutoSize = true;
            this.label38.Location = new System.Drawing.Point(23, 32);
            this.label38.Name = "label38";
            this.label38.Size = new System.Drawing.Size(23, 18);
            this.label38.TabIndex = 7;
            this.label38.Text = "x0";
            // 
            // progressBar1
            // 
            this.progressBar1.Location = new System.Drawing.Point(3, 384);
            this.progressBar1.Name = "progressBar1";
            this.progressBar1.Size = new System.Drawing.Size(692, 23);
            this.progressBar1.TabIndex = 1;
            this.progressBar1.Click += new System.EventHandler(this.progressBar1_Click);
            // 
            // timer1
            // 
            this.timer1.Tick += new System.EventHandler(this.timer1_Tick_1);
            // 
            // timer2
            // 
            this.timer2.Tick += new System.EventHandler(this.timer2_Tick);
            // 
            // PS5000ABlockForm
            // 
            this.ClientSize = new System.Drawing.Size(702, 419);
            this.Controls.Add(this.progressBar1);
            this.Controls.Add(this.tabControl1);
            this.Name = "PS5000ABlockForm";
            this.tabControl1.ResumeLayout(false);
            this.tabPage1.ResumeLayout(false);
            this.tabPage1.PerformLayout();
            this.tabPage3.ResumeLayout(false);
            this.tabPage3.PerformLayout();
            this.tabPage2.ResumeLayout(false);
            this.tabPage2.PerformLayout();
            this.tabPage4.ResumeLayout(false);
            this.tabPage4.PerformLayout();
            this.tabPage5.ResumeLayout(false);
            this.tabPage5.PerformLayout();
            this.tabPage6.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.tabPage7.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.chart1)).EndInit();
            this.tabPage8.ResumeLayout(false);
            this.tabPage9.ResumeLayout(false);
            this.tabPage9.PerformLayout();
            this.tabPage10.ResumeLayout(false);
            this.tabPage10.PerformLayout();
            this.tabPage11.ResumeLayout(false);
            this.tabPage11.PerformLayout();
            this.ResumeLayout(false);

        }

        private void progressBar1_Click(object sender, EventArgs e)
        {

        }
        public void CollectData()
        {
            uint samples1 = uint.Parse(textBox13.Text);
            uint samples2 = uint.Parse(textBox10.Text);
            uint count_avg = uint.Parse(textBox11.Text);
            masA = new long[samples1 + samples2];
            arrA = new double[samples1 + samples2];
            all = int.Parse(textBox11.Text);

            ushort RANGE_ = inputRanges[comboRangeA.SelectedIndex];

            double mult = 1.0 / 2.0 / (double)count_avg * RANGE_ / 65536.0;

            for (uint j = 0; j < samples1 + samples2; j++)
            {
                arrA[j] = 0;
            }

            for (uint i = 0; i < count_avg; i++)
            {
                if (stop_flag)
                {
                    break;
                }
                save = (int)i + 1;
                lock (_captureLock) { start(samples1, samples2, 1); }
                if ((i % 10) == 1)
                {

                    System.Windows.Forms.Application.DoEvents();
                }

                if (checkBox20.Checked)
                {
                    if ((i % 100) == 0)
                    {
                        for (uint j = 0; j < samples1 + samples2; j++)
                        {
                            if (stop_flag)
                            {
                                break;
                            }
                            arrA[j] = (double)masA[j] * mult;

                        }

                        VisualaseAsync(Color.Blue, arrA, 1);
                    }
                }
            }



            for (uint j = 0; j < samples1 + samples2; j++)
            {
                arrA[j] = (double)masA[j] * mult; ;
            }


        }

        private double FindAvg(double[] data)
        {
            double a = 0;
            for (int i = 0; i < data.Length; i++)
            {
                a += data[i];
            }
            return a / (double)data.Length;
        }
        private long FindAvg(long[] data)
        {
            double a = 0;
            for (int i = 0; i < data.Length; i++)
            {
                a += (double)data[i];
            }
            double avg_ = (double)a / (double)data.Length;
            return (long)avg_;
        }

        private void SuppressSpikes(double[] data, int index)
        {
            double A = FindAvg(data);
            for (int i = 0; i < index; i++)
            {
                data[i] = A;
            }
        }

        private void NoOffset(double[] data)
        {
            double A = FindAvg(data);
            for (int i = 0; i < data.Length; i++)
            {
                data[i] -= A;
            }
        }
        private void button2_Click(object sender, EventArgs e)
        {
            string dir = textBox3.Text;
            if (dir[dir.Length - 1] != '\\')
            {
                dir = String.Concat(dir, "\\");
            }

            Directory.CreateDirectory(dir);

            int step_i = int.Parse(textBox19.Text);
            timer1.Enabled = true;

            uint samples1 = uint.Parse(textBox13.Text);
            uint samples2 = uint.Parse(textBox10.Text);

            CollectData();
            ////////for (uint i = 0; i < samples1 + samples2; i++)
            ////////{
            ////////    if (stop_flag)
            ////////    {
            ////////        break;
            ////////    }
            ////////    arrA[i] = (double)masA[i] / 2.0 / (double)uint.Parse(textBox11.Text) * inputRanges[comboRangeA.SelectedIndex] / 65536.0;
            ////////    if (checkBox2.Checked)
            ////////    {
            ////////        //      SuppressSpikes(arrA, int.Parse(textBox14.Text));
            ////////    }
            ////////}


            if (checkBox2.Checked)
            {
                SuppressSpikes(arrA, int.Parse(textBox14.Text));
            }

            // Visualase(Color.Red, arrA);

            bool _reason = checkBox2.Checked || checkBox3.Checked || checkBox4.Checked;
            if (_reason)
            {
                if (checkBox2.Checked)
                {
                    SuppressSpikes(arrA, int.Parse(textBox14.Text));
                }
                if (checkBox3.Checked)
                {
                    RunAvg(ref arrA, int.Parse(textBox2.Text));
                };
                if (checkBox4.Checked)
                {
                    NoOffset(arrA);
                };

            }

            if (checkBox8.Checked)
            {
                if (textBox15.Text.Length > 0)
                {
                    Complex[] filtr = WorkWithFiles.LoadFromFileC(textBox15.Text);
                    arrA = FurieTrans.FuncMult(arrA, oscilloscope_timestep, -oscilloscope_timestep * double.Parse(textBox13.Text), filtr);
                }

            }


            if (checkBox9.Checked)
            {
                if (textBox16.Text.Length > 0)
                {
                    Complex[] f = FurieTrans.FurieTransf(arrA, oscilloscope_timestep, -oscilloscope_timestep * double.Parse(textBox13.Text),
                         double.Parse(textBox4.Text), double.Parse(textBox5.Text), int.Parse(textBox6.Text));
                    Complex[] filtr = WorkWithFiles.LoadFromFileC(textBox16.Text);
                    f = FurieTrans.FuncMult(f, double.Parse(textBox5.Text), double.Parse(textBox4.Text), filtr);
                    Complex[] restored = FurieTrans.FurieTransfReverse(f, oscilloscope_timestep, -oscilloscope_timestep * double.Parse(textBox13.Text), arrA.Length, double.Parse(textBox4.Text), double.Parse(textBox5.Text));
                    for (int k1 = 0; k1 < restored.Length; k1++)
                    {
                        arrA[k1] = restored[k1].Real * 2;//важно делать умножение на 2 так как интеграл по полубесконечному промежутку
                    }
                }


            }

            if (checkBox4.Checked)
            {
                NoOffset(arrA);
            };

            timer1.Enabled = false;
            //dir = String.Concat(dir, "Measurement.txt");
            dir = String.Concat(dir, textBox20.Text);
            WorkWithFiles.Save2File(dir, arrA);
            PublishCapture(arrA);
            if (stop_flag)
            {
                save = 0; stop_flag = false;
            }



            //Visualase(Color.Red, arrA, 5);

        }

        private void timer1_Tick_1(object sender, EventArgs e)
        {
            //    Invalidate();
            System.Windows.Forms.Application.DoEvents();

        }

        private void button3_Click(object sender, EventArgs e)
        {
            try
            {
                //  Switch1.SendCmd(0, int.Parse(textBox1.Text));
                Switch1.OUTRelay(int.Parse(textBox1.Text));
                //for (int k1 = 0; k1 < Switch1.Kirill_DATA.Length; k1++)
                //{
                //    string ssss = Convert.ToString(Switch1.Kirill_DATA[k1], 2);
                //    ssss = Switch1.Kirill_DATA[k1].ToString();
                //    textBoxUnitInfo.AppendText(ssss + Environment.NewLine); 
                //}

                //Switch1.sendKIRILL();
                //Thread.Sleep(500);
                //string txt = Switch1.GetAcceptedKiril();

                string txt = Switch1.Protocol_V2d0_send();
                textBoxUnitInfo.AppendText(txt + Environment.NewLine);
                this.Text = txt;

            }
            catch (Exception)
            {
                MessageBox.Show("Не удалось послать команду");
            }
        }

        private void button5_Click(object sender, EventArgs e)
        {
            try
            {
                // Switch1.SendCmd(2, int.Parse(textBox1.Text));
                Switch1.OFFRelay(int.Parse(textBox1.Text));
                //Switch1.sendKIRILL();
                //Thread.Sleep(500);
                //string txt = Switch1.GetAcceptedKiril();

                string txt = Switch1.Protocol_V2d0_send();

                textBoxUnitInfo.AppendText(txt + "\n");
                this.Text = txt;
            }
            catch (Exception)
            {
                MessageBox.Show("Не удалось послать команду");
            }
        }

        private void button4_Click(object sender, EventArgs e)
        {
            try
            {
                Switch1.INRelay(int.Parse(textBox1.Text));
                //for (int k1 = 0; k1 < Switch1.Kirill_DATA.Length; k1++)
                //{
                //    string ssss = Convert.ToString(Switch1.Kirill_DATA[k1], 2);
                //    ssss = Switch1.Kirill_DATA[k1].ToString();
                //    textBoxUnitInfo.AppendText(ssss + Environment.NewLine);

                //}
                //Switch1.sendKIRILL();
                ////  Switch1.SendCmd(1, int.Parse(textBox1.Text));
                //Thread.Sleep(500);
                //string txt = Switch1.GetAcceptedKiril();


                string txt = Switch1.Protocol_V2d0_send();

                textBoxUnitInfo.AppendText(txt + Environment.NewLine);
                this.Text = txt;
            }
            catch (Exception)
            {
                MessageBox.Show("Не удалось послать команду");
            }
        }

        private string[] names_;
        private void button6_Click(object sender, EventArgs e)
        {
            names_ = SerialPort.GetPortNames();
            listBox1.Items.Clear();
            listBox1.Items.AddRange(names_);

            if (listBox1.Items.Count > 0)
            {
                listBox1.SelectedIndex = listBox1.Items.Count - 1;
            }
        }

        private void button7_Click(object sender, EventArgs e)
        {
            if (Switch1 == null)
            {

                Switch1 = new Switch();
                //Switch1.OpenPort(listBox1.SelectedIndex);
                string com_id = listBox1.Items[listBox1.SelectedIndex].ToString();
                string com_id2 = com_id.Substring(3);
                int c_id = int.Parse(com_id2);

                Switch1.InitKirill(int.Parse(textBox21.Text), listBox1.SelectedIndex);

                //while (Switch1.port.BytesToRead==0)
                //    Thread.Sleep(100);

                //Thread.Sleep(10);

                //string txt = Switch1.ReadALL();
                //textBoxUnitInfo.AppendText(txt + "\n");
                //this.Text += txt;
                //switch_connected = true;

                //Switch1.sendKIRILL();

                //while (Switch1.port.BytesToRead == 0)
                //    Thread.Sleep(100);

                //Thread.Sleep(10);

                //txt = Switch1.ReadALL();
                //textBoxUnitInfo.AppendText(txt + "\n");

                Thread.Sleep(1000);
                string txt = Switch1.Protocol_V2d0_connect(byte.Parse(textBox21.Text));
                textBoxUnitInfo.AppendText(txt + "\n");
                //send zeros from inited buff
                txt = Switch1.Protocol_V2d0_send();
                textBoxUnitInfo.AppendText(txt + "\n");


            }
            else
            {
                string txt = Switch1.Protocol_V2d0_disconnect();
                textBoxUnitInfo.AppendText(txt + "\n");
                Switch1 = null;

            }
        }

        private void button8_Click(object sender, EventArgs e)
        {
            //   button2.Text = "olkjnj";
            if (!stop_flag)
            {
                stop_flag = true;
            }

            timer1.Enabled = false;
        }
        private static void RunAvg(ref double[] Array_, int kernel_len = 10)
        {
            double kl = kernel_len;
            double[] Array_buf = new double[Array_.Length];
            for (int i = kernel_len + 1; i < Array_.Length - (kernel_len + 1); i++)
            {
                double summ = 0;
                for (int j = -kernel_len; j < kernel_len / 2; j++)
                {
                    summ += Array_[i + j];
                }
                Array_buf[i] = summ / kl;
            }
            for (int i = kernel_len + 1; i < Array_.Length - (kernel_len + 1); i++)
            {
                Array_[i] = Array_buf[i];
            }

        }
        private void button9_Click(object sender, EventArgs e)
        {
            RunAvg(ref arrA, int.Parse(textBox2.Text));
            // Visualase(Color.Red, arrA, 5);
            string path = String.Concat(@"C:\Temp\", DateTime.Now.ToString().Replace(':', '_'), "TempCapture.txt");

            WorkWithFiles.Save2File(path, arrA);

        }

        private void tabPage6_Click(object sender, EventArgs e)
        {
            if (arrA != null)
            {
                // Visualase(Color.Red, arrA, 5);
            }
        }

        private void tabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            //if (arrA != null)
            //{
            //    Visualase(Color.Red, arrA, 5);
            //}
        }

        private void button11_Click(object sender, EventArgs e)
        {
            if (switch_connected)
            {
                string dir = textBox3.Text;
                if (dir[dir.Length - 1] != '\\')
                {
                    dir = String.Concat(dir, "\\");
                }

                Directory.CreateDirectory(dir);

                int step_i = int.Parse(textBox19.Text);
                //==============================================================
                //вывод файла с временами
                if (checkBox10.Checked)
                {
                    int l = int.Parse(textBox10.Text) + int.Parse(textBox13.Text);
                    double[] dt = new double[l];
                    double n0 = double.Parse(textBox13.Text);
                    for (int i = 0; i < l; i += step_i)
                    {
                        dt[i] = oscilloscope_timestep * i - oscilloscope_timestep * n0;
                    }
                    string fn = "times.txt";
                    WorkWithFiles.Save2File(String.Concat(dir, fn), dt, step_i);
                }

                //==============================================================


                for (int i = 0; i < checkedListBox1.CheckedIndices.Count; i++)
                {
                    int j = checkedListBox1.CheckedIndices[i];

                    {
                        string txt = Switch1.GetAcceptedKiril();
                        textBoxUnitInfo.AppendText(txt + "\n");
                        this.Text = txt;

                        Switch1.OFFRelay(j);
                        Switch1.OUTRelay(j);
                        Switch1.sendKIRILL();

                        // Switch1.SendCmd(0, j);
                        while (Switch1.port.BytesToRead == 0) { Thread.Sleep(50); };
                        txt = Switch1.GetAcceptedKiril();
                        textBoxUnitInfo.AppendText(txt + "\n");
                        this.Text = txt;
                        // Thread.Sleep(500);// подождем пока напряжение устаканится на ПЭ
                        for (int k = 0; k < checkedListBox2.CheckedIndices.Count; k++)
                        {

                            int m = checkedListBox2.CheckedIndices[k];
                            if ((m != j))

                            {

                                txt = Switch1.GetAcceptedKiril();
                                textBoxUnitInfo.AppendText(txt + "\n");
                                this.Text = txt;

                                Switch1.OFFRelay(m);
                                Switch1.INRelay(m);
                                Switch1.sendKIRILL();

                                //Switch1.SendCmd(1, m);
                                while (Switch1.port.BytesToRead == 0) { Thread.Sleep(50); };
                                txt = Switch1.GetAcceptedKiril();
                                textBoxUnitInfo.AppendText(txt + "\n");
                                this.Text = txt;

                                timer1.Enabled = true;

                                CollectData();

                                if (checkBox13.Checked)
                                {
                                    dir = String.Concat(textBox3.Text, CODES[j], "\\");
                                    Directory.CreateDirectory(dir);
                                    string fn_ = String.Concat("raw_", CODES[j], "2", CODES[m], ".txt");
                                    WorkWithFiles.Save2File(String.Concat(dir, fn_), arrA, step_i);
                                }

                                bool _reason = checkBox2.Checked || checkBox3.Checked || checkBox4.Checked;
                                if (_reason)
                                {
                                    if (checkBox2.Checked)
                                    {
                                        SuppressSpikes(arrA, int.Parse(textBox14.Text));
                                    }
                                    if (checkBox3.Checked)
                                    {
                                        RunAvg(ref arrA, int.Parse(textBox2.Text));
                                    };
                                    if (checkBox4.Checked)
                                    {
                                        NoOffset(arrA);
                                    };
                                    timer1.Enabled = false;
                                    if (stop_flag)
                                    {
                                        save = 0; stop_flag = false;
                                    }
                                }
                                //============================================================
                                //вставить применение фильтра
                                if (checkBox8.Checked)
                                {
                                    if (textBox15.Text.Length > 0)
                                    {
                                        Complex[] filtr = WorkWithFiles.LoadFromFileC(textBox15.Text);
                                        arrA = FurieTrans.FuncMult(arrA, oscilloscope_timestep, -oscilloscope_timestep * double.Parse(textBox13.Text), filtr);
                                    }

                                    dir = String.Concat(textBox3.Text, CODES[j], "\\");
                                    Directory.CreateDirectory(dir);
                                    string fn_ = String.Concat("afterf1_", CODES[j], "2", CODES[m], ".txt");
                                    WorkWithFiles.Save2File(String.Concat(dir, fn_), arrA, step_i);
                                }





                                if (checkBox9.Checked)
                                {
                                    if (textBox16.Text.Length > 0)
                                    {
                                        Complex[] f = FurieTrans.FurieTransf(arrA, oscilloscope_timestep, -oscilloscope_timestep * double.Parse(textBox13.Text),
                                             double.Parse(textBox4.Text), double.Parse(textBox5.Text), int.Parse(textBox6.Text));
                                        Complex[] filtr = WorkWithFiles.LoadFromFileC(textBox16.Text);
                                        f = FurieTrans.FuncMult(f, double.Parse(textBox5.Text), double.Parse(textBox4.Text), filtr);
                                        Complex[] restored = FurieTrans.FurieTransfReverse(f, oscilloscope_timestep, -oscilloscope_timestep * double.Parse(textBox13.Text), arrA.Length, double.Parse(textBox4.Text), double.Parse(textBox5.Text));
                                        for (int k1 = 0; k1 < restored.Length; k1++)
                                        {
                                            arrA[k1] = restored[k1].Real * 2;//важно делать умножение на 2 так как интеграл по полубесконечному промежутку
                                        }
                                    }
                                    dir = String.Concat(textBox3.Text, CODES[j], "\\");
                                    Directory.CreateDirectory(dir);
                                    string fn_ = String.Concat("afterf2_", CODES[j], "2", CODES[m], ".txt");
                                    WorkWithFiles.Save2File(String.Concat(dir, fn_), arrA, step_i);

                                }



                                Visualase(Color.Red, arrA, 5);
                                ///===========================================================

                                //   Save2File(String.Concat(textBox3.Text,"ft_", CODES[j], "2", CODES[m], ".txt"), abs_f);
                                dir = String.Concat(textBox3.Text, CODES[j], "\\");
                                Directory.CreateDirectory(dir);
                                string fn = String.Concat(CODES[j], "2", CODES[m], ".txt");
                                WorkWithFiles.Save2File(String.Concat(dir, fn), arrA, step_i);
                                if (checkBox5.Checked)
                                {
                                    Complex[] f = FurieTrans.FurieTransf(arrA, oscilloscope_timestep, -oscilloscope_timestep * double.Parse(textBox13.Text), 1 * double.Parse(textBox4.Text), double.Parse(textBox5.Text), int.Parse(textBox6.Text));
                                    WorkWithFiles.Save2File(String.Concat(dir, "f_", fn), f, step_i);
                                    if (checkBox6.Checked)
                                    {
                                        double[] abs_f = new double[f.Length];
                                        for (int k1 = 0; k1 < f.Length; k1++)
                                        {
                                            abs_f[k1] = f[k1].Magnitude;
                                        }
                                        WorkWithFiles.Save2File(String.Concat(dir, "abs_f_", fn), abs_f, step_i);
                                    }

                                }
                            }
                        }
                    }
                }
            }
        }

        private void button12_Click(object sender, EventArgs e)
        {
            try
            {
                //Создаём или перезаписываем существующий файл
                string Path_ = textBox12.Text;
                if (Path_[Path_.Length - 1] != '\\')
                {
                    Path_ = String.Concat(Path_, "\\");
                }

                Directory.CreateDirectory(Path_);

                StreamWriter sw = File.CreateText(String.Concat(Path_, "info.txt"));
                //Записываем текст в поток файла
                sw.WriteLine(textBox17.Text);
                //Закрываем файл
                sw.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);
            }
            for (int i = 0; i < checkedListBox1.CheckedIndices.Count; i++)
            {
                int j = checkedListBox1.CheckedIndices[i];
                for (int k = 0; k < checkedListBox2.CheckedIndices.Count; k++)
                {
                    int m = checkedListBox2.CheckedIndices[k];
                    if (m != j)
                    {
                        string Path_ = textBox7.Text;
                        if (Path_[Path_.Length - 1] != '\\')
                        {
                            Path_ = String.Concat(Path_, "\\");
                        }

                        Directory.CreateDirectory(Path_);
                        string dir1 = String.Concat(Path_, CODES[j], "\\");
                        Path_ = textBox8.Text;
                        if (Path_[Path_.Length - 1] != '\\')
                        {
                            Path_ = String.Concat(Path_, "\\");
                        }

                        Directory.CreateDirectory(Path_);
                        string dir2 = String.Concat(Path_, CODES[j], "\\");
                        Path_ = textBox12.Text;
                        if (Path_[Path_.Length - 1] != '\\')
                        {
                            Path_ = String.Concat(Path_, "\\");
                        }

                        Directory.CreateDirectory(Path_);
                        string dir3 = String.Concat(Path_, CODES[j], "\\");


                        string fn = String.Concat(CODES[j], "2", CODES[m], ".txt");
                        StreamReader R1 = new StreamReader(String.Concat(dir1, fn));
                        StreamReader R2 = new StreamReader(String.Concat(dir2, fn));
                        int l1 = int.Parse(textBox10.Text) + int.Parse(textBox13.Text);
                        //int l2 = int.Parse(R2.ReadLine());
                        int l = l1;
                        //if (l > l2)
                        //{
                        //    l = l2;
                        //}
                        Directory.CreateDirectory(dir3);
                        using (StreamWriter Writer = new StreamWriter(String.Concat(dir3, fn)))
                        {
                            //   Writer.WriteLine(l);
                            double[] buf = new double[l];

                            for (int n = 0; n < l; n++)
                            {
                                string s1 = R1.ReadLine().Replace('.', ',');
                                double d1 = double.Parse(s1);
                                string s2 = R2.ReadLine().Replace('.', ','); ;
                                double d2 = double.Parse(s2);
                                buf[n] = d1 - d2;
                            }
                            if (checkBox11.Checked)
                            {
                                int pod = int.Parse(textBox18.Text);
                                for (int n = 0; n < pod; n++)
                                {
                                    buf[n] = 0;
                                }
                            }
                            if (checkBox7.Checked)
                            {
                                double maxc_ = 0;
                                for (int n = 0; n < l; n++)
                                {
                                    double a = Math.Abs(buf[n]);
                                    if (a > maxc_)
                                    {
                                        maxc_ = a;
                                    }
                                }
                                for (int n = 0; n < l; n++)
                                {
                                    buf[n] = buf[n] / maxc_;
                                }
                            }

                            if (checkBox12.Checked)
                            {

                                if (textBox16.Text.Length > 0)
                                {


                                    oscilloscope_timestep = double.Parse(textBox9.Text);
                                    if (oscilloscope_timestep < 4.0)
                                    {
                                        throw new Exception();
                                    }

                                    oscilloscope_timestep = (oscilloscope_timestep - 3.0) / 62500000.0;


                                    Complex[] f = FurieTrans.FurieTransf(buf, oscilloscope_timestep, -oscilloscope_timestep * double.Parse(textBox13.Text),
                                         double.Parse(textBox4.Text), double.Parse(textBox5.Text), int.Parse(textBox6.Text));
                                    Complex[] filtr = WorkWithFiles.LoadFromFileC(textBox16.Text);
                                    f = FurieTrans.FuncMult(f, double.Parse(textBox5.Text), double.Parse(textBox4.Text), filtr);
                                    Complex[] restored = FurieTrans.FurieTransfReverse(f, oscilloscope_timestep, -oscilloscope_timestep * double.Parse(textBox13.Text),
                                        buf.Length, double.Parse(textBox4.Text), double.Parse(textBox5.Text));
                                    for (int k1 = 0; k1 < restored.Length; k1++)
                                    {
                                        buf[k1] = restored[k1].Real * 2;//важно делать умножение на 2 так как интеграл по полубесконечному промежутку
                                    }
                                }
                                Directory.CreateDirectory(dir3);
                                // fn = String.Concat("afterf2_", CODES[j], "2", CODES[m], ".txt");
                                fn = String.Concat(CODES[j], "2", CODES[m], ".txt");


                            }


                            for (int n = 0; n < l; n++)
                            {
                                Writer.WriteLine(buf[n].ToString().Replace(',', '.'));
                            }
                            Writer.Flush();
                            Writer.Close();
                        }
                        //while  (!(R1.EndOfStream || R2.EndOfStream  ))
                        //    {
                        //}
                    }
                }
            }
        }

        //private Complex Str2Cmpl(string s)
        //{
        //    int pos = s.IndexOf(".0.") + 2;
        //    string s1 = s.Substring(1, pos - 1).Replace('.', ',');
        //    string s2 = s.Substring(pos + 1, s.Length - pos - 3).Replace('.', ',');
        //    Complex r = new Complex(double.Parse(s1), double.Parse(s2));
        //    return r;
        //}
        private void button13_Click(object sender, EventArgs e)
        {

        }

        private void label13_Click(object sender, EventArgs e)
        {

        }

        private void button13_Click_1(object sender, EventArgs e)
        {

            for (int k = 0; k < checkedListBox1.Items.Count; k++)
            {
                checkedListBox1.SetItemChecked(k, !checkedListBox1.GetItemChecked(k));
            }
            for (int k = 0; k < checkedListBox2.Items.Count; k++)
            {
                checkedListBox2.SetItemChecked(k, !checkedListBox2.GetItemChecked(k));
            }

        }

        private void label17_Click(object sender, EventArgs e)
        {

        }

        private void button14_Click(object sender, EventArgs e)
        {
            if (DialogResult.OK == folderBrowserDialog1.ShowDialog())
            {
                textBox3.Text = folderBrowserDialog1.SelectedPath;
            }
        }

        private void textBox3_TextChanged(object sender, EventArgs e)
        {

        }

        private void button15_Click(object sender, EventArgs e)
        {
            button15.Text = "sasa";
            if (fileMFT == null)
            {
                fileMFT = new FileMFT();

            }
            //fileMFT.LoadFile(@"C: \Users\user1\Desktop\Новая папка\111.txt");
            //fileMFT.SaveFile(@"C: \Users\user1\Desktop\Новая папка\asfasasfs.txt");
            //fileMFT.SetName(@"C:\IMMI\Team\Bareiko\2020\ZAMERI\___________________TYOMICH_OMNOMNOM\10mm_257_54_razn\B\B2E.txt");
            //fileMFT.LoadTXT();

            //fileMFT.SetName(@"C:\IMMI\Team\Bareiko\2020\ZAMERI\___________________TYOMICH_OMNOMNOM\10mm_257_54_razn\B\B2E.mft");
            //fileMFT.SaveMFT();
            //fileMFT.SetName(@"C:\IMMI\Team\Bareiko\2020\ZAMERI\___________________TYOMICH_OMNOMNOM\10mm_257_54_razn\B\B2E.mftz");
            //fileMFT.SaveMFTZ();

            ////fileMFT.SetName(@"C:\IMMI\Team\Bareiko\2020\ZAMERI\___________________TYOMICH_OMNOMNOM\10mm_257_54_razn\B\test_z.mftz");
            ////fileMFT.LoadMFTZ();
            ////fileMFT.SetName(@"C:\IMMI\Team\Bareiko\2020\ZAMERI\___________________TYOMICH_OMNOMNOM\10mm_257_54_razn\B\test_z.txt");
            //// fileMFT.SaveTXT();

            //fileMFT.SetName(@"C: \Users\user1\Desktop\Новая папка\leatherman.txt");
            //fileMFT.SetSize(5);
            //fileMFT.SetVal(0, 0);
            //fileMFT.SetVal(1, 1);
            //fileMFT.SetVal(2, 2);
            //fileMFT.SetVal(3, 3);
            //fileMFT.SetVal(4, 4);

            //fileMFT.SaveMFT();

            //fileMFT.SetName(@"C: \Users\user1\Desktop\Новая папка\leatherman.txt");
            //fileMFT.LoadMFT();



            //fileMFT.SetName(@"C:\IMMI\Team\Bareiko\2020\ZAMERI\___________________TYOMICH_OMNOMNOM\10mm_257_54_razn\B\B2E.txt");
            //fileMFT.LoadTXT();

            //fileMFT.SetName(@"C:\IMMI\Team\Bareiko\2020\ZAMERI\___________________TYOMICH_OMNOMNOM\10mm_257_54_razn\B\B2E.mftd");
            //fileMFT.SaveMFTD();

            fileMFT.SetName(@"C:\IMMI\Team\Bareiko\2020\ZAMERI\___________________TYOMICH_OMNOMNOM\10mm_257_54_razn\B\B2E.mftd");
            fileMFT.LoadMFTD();
            fileMFT.SetName(@"C:\IMMI\Team\Bareiko\2020\ZAMERI\___________________TYOMICH_OMNOMNOM\10mm_257_54_razn\B\test_z.txt");
            fileMFT.SaveTXT();



        }

        private void button16_Click(object sender, EventArgs e)
        {
            //string txt1 = Switch1.ReadALL(); 

            for (int i = 0; i < Switch1.Kirill_DATA.Length; i++)
            {
                Switch1.Kirill_DATA[i] = 0;
            }
            //for (int k1 = 0; k1 < Switch1.Kirill_DATA.Length; k1++)
            //{
            //    string ssss = Convert.ToString(Switch1.Kirill_DATA[k1], 2);
            //    ssss = Switch1.Kirill_DATA[k1].ToString();
            //    textBoxUnitInfo.AppendText(ssss + Environment.NewLine);

            //}

            //Switch1.sendKIRILL();
            //while( Switch1.port.BytesToRead == 0 )
            //{ Thread.Sleep(100); }

            //string txt = Switch1.ReadALL();

            string txt = Switch1.Protocol_V2d0_send();
            textBoxUnitInfo.AppendText(txt + Environment.NewLine);
            this.Text = txt;
        }

        private void button17_Click(object sender, EventArgs e)
        {
            oscilloscope_timestep = double.Parse(textBox9.Text);
            if (oscilloscope_timestep < 4.0)
            {
                throw new Exception();
            }
            oscilloscope_timestep = (oscilloscope_timestep - 3.0) / 62500000.0;

            //Написать  метод для считающий фурье
            for (int i = 0; i < checkedListBox1.CheckedIndices.Count; i++)
            {
                int j = checkedListBox1.CheckedIndices[i];
                for (int k = 0; k < checkedListBox2.CheckedIndices.Count; k++)
                {
                    int m = checkedListBox2.CheckedIndices[k];
                    if (m != j)
                    {

                        // длинна замера
                        int l1 = int.Parse(textBox10.Text) + int.Parse(textBox13.Text);
                        int l = l1;

                        string Path_ = textBox22.Text;
                        if (Path_[Path_.Length - 1] != '\\')
                        {
                            Path_ = String.Concat(Path_, "\\");
                        }

                        //Открываем файл для ввода
                        string fn = String.Concat(CODES[j], "2", CODES[m], ".txt");
                        string path_ = String.Concat(Path_, CODES[j], '\\', fn);
                        StreamReader R1 = new StreamReader(path_);
                        double[] buf = new double[l];
                        for (int n = 0; n < l; n++)
                        {
                            string s1 = R1.ReadLine().Replace('.', ',');
                            buf[n] = double.Parse(s1); ;
                        }

                        //Преобразования фурье 
                        Complex[] f = FurieTrans.FurieTransf(buf, oscilloscope_timestep, -oscilloscope_timestep * double.Parse(textBox13.Text),
                             double.Parse(textBox4.Text), double.Parse(textBox5.Text), int.Parse(textBox6.Text));

                        //Открываем файл для вывода
                        path_ = String.Concat(Path_, CODES[j], '\\', "furie_", fn);
                        using (StreamWriter Writer = new StreamWriter(path_))
                        {
                            for (int n = 0; n < f.Length; n++)
                            {
                                Writer.WriteLine(f[n].ToString().Replace(',', '.').Replace(". ", ", "));
                            }
                            Writer.Flush();
                            Writer.Close();
                        }

                        //Открываем файл для вывода
                        path_ = String.Concat(Path_, CODES[j], '\\', "abs_furie_", fn);
                        using (StreamWriter Writer = new StreamWriter(path_))
                        {
                            for (int n = 0; n < f.Length; n++)
                            {
                                Writer.WriteLine(f[n].Magnitude.ToString().Replace(',', '.').Replace(". ", ", "));
                            }
                            Writer.Flush();
                            Writer.Close();
                        }

                    }
                }
            }
        }

        private void button18_Click(object sender, EventArgs e)
        {
            string cmd = String.Concat(textBox23.Text, ' ', textBox24.Text, ' ', textBox25.Text);
            Switch1.SendCmd(cmd);
            //   textBoxUnitInfo.AppendText(Switch1.GetAccepted());
        }

        private void button20_Click(object sender, EventArgs e)
        {
            names_ = SerialPort.GetPortNames();
            listBox2.Items.Clear();
            listBox2.Items.AddRange(names_);

            if (listBox2.Items.Count > 0)
            {
                listBox2.SelectedIndex = listBox1.Items.Count - 1;
            }
        }

        private void button19_Click(object sender, EventArgs e)
        {
            Switch1 = new Switch();
            string com_id = listBox2.Items[listBox2.SelectedIndex].ToString();
            //     string com_id2 = com_id.Substring(3);
            int baudrate = int.Parse(textBox15.Text);
            Switch1.OpenPort(com_id, baudrate);

            // Thread.Sleep(500);

            //  string txt = Switch1.GetAcceptedKiril();
            //  textBox27.AppendText(txt + "\n");


            switch_connected = true;
            timer2.Interval = 50;
            timer2.Start();
            string sss = String.Concat("px = ", cnc_x.ToString(), " py = ", cnc_y.ToString());
            label34.Text = sss;



        }

        private void listBox2_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void button21_Click(object sender, EventArgs e)
        {
            string cmd = textBox26.Text + (char)13 + (char)10;
            Switch1.SendCmd(cmd);

            //  Thread.Sleep(500);

            //  string txt = Switch1.GetAcceptedKiril();
            //  textBox27.AppendText(txt + "\n");

            ////  textBoxUnitInfo.AppendText(Switch1.GetAccepted());
        }

        private void timer2_Tick(object sender, EventArgs e)
        {
            if (Switch1 != null)
                if (Switch1.port.IsOpen)
                    if (Switch1.port.BytesToRead > 0)
                    {
                        string txt = Switch1.GetAcceptedKiril();
                        textBox27.AppendText(txt + "\n");
                    }
        }

        private void button24_Click(object sender, EventArgs e)
        {
            cnc_x = cnc_x - float.Parse(textBox29.Text);
            string sss = String.Concat("g0 x", cnc_x.ToString(), " y", cnc_y.ToString());
            string cmd = sss + (char)13 + (char)10;
            Switch1.SendCmd(cmd);
            sss = String.Concat("px = ", cnc_x.ToString(), " py = ", cnc_y.ToString());
            label34.Text = sss;

        }

        private void button25_Click(object sender, EventArgs e)
        {
            cnc_x = cnc_x + float.Parse(textBox29.Text);
            string sss = String.Concat("g0 x", cnc_x.ToString(), " y", cnc_y.ToString());
            string cmd = sss + (char)13 + (char)10;
            Switch1.SendCmd(cmd);
            sss = String.Concat("px = ", cnc_x.ToString(), " py = ", cnc_y.ToString());
            label34.Text = sss;
        }

        private void button22_Click(object sender, EventArgs e)
        {
            cnc_y = cnc_y + float.Parse(textBox28.Text);
            string sss = String.Concat("g0 x", cnc_x.ToString(), " y", cnc_y.ToString());
            string cmd = sss + (char)13 + (char)10;
            Switch1.SendCmd(cmd);
            sss = String.Concat("px = ", cnc_x.ToString(), " py = ", cnc_y.ToString());
            label34.Text = sss;
        }

        private void button23_Click(object sender, EventArgs e)
        {
            cnc_y = cnc_y - float.Parse(textBox28.Text);
            string sss = String.Concat("g0 x", cnc_x.ToString(), " y", cnc_y.ToString());
            string cmd = sss + (char)13 + (char)10;
            Switch1.SendCmd(cmd);
            sss = String.Concat("px = ", cnc_x.ToString(), " py = ", cnc_y.ToString());
            label34.Text = sss;
        }

        private void button26_Click(object sender, EventArgs e)
        {
            float nx, ny, dx, dy, lx, ly, x0, y0, x1, y1, feed_rate;

            uint samples1 = uint.Parse(textBox13.Text);
            uint samples2 = uint.Parse(textBox10.Text);
            uint count_avg = uint.Parse(textBox11.Text);

            x0 = float.Parse(textBox31.Text);
            y0 = float.Parse(textBox30.Text);
            x1 = float.Parse(textBox33.Text);
            y1 = float.Parse(textBox32.Text);

            nx = float.Parse(textBox35.Text);
            ny = float.Parse(textBox34.Text);
            lx = x1 - x0;
            ly = y1 - y0;
            dx = lx / nx;
            dy = ly / ny;

            cnc_x = x0;
            cnc_y = y0;

            feed_rate = float.Parse(textBox38.Text);

            string cmd;

            // string sss = String.Concat("g01 x", cnc_x.ToString().Replace(',', '.'), " y", cnc_y.ToString().Replace(',', '.'), " f", feed_rate.ToString().Replace(',', '.'));


            if (checkBox17.Checked || checkBox16.Checked)
            {

                string sss = "g01";
                if (checkBox16.Checked)
                {
                    sss = String.Concat(sss, " x", cnc_x.ToString().Replace(',', '.'));

                }
                if (checkBox17.Checked)
                {
                    sss = String.Concat(sss, " y", cnc_y.ToString().Replace(',', '.'));
                }
                sss = String.Concat(sss, " f", feed_rate.ToString().Replace(',', '.'));


                cmd = sss + (char)13 + (char)10;
                Switch1.SendCmd(cmd);
                //     sss = String.Concat("px = ", cnc_x.ToString().Replace(',', '.'), " py = ", cnc_y.ToString().Replace(',', '.'));
                label34.Text = cmd;
                Thread.Sleep(250);

                int ix = 0;
                //    for (cnc_x = x0; cnc_x <= x1; cnc_x += dx)
                for (cnc_x = x0; (cnc_x <= x1) && (ix < nx); cnc_x += dx, ix++)

                {
                    int iy = 0;

                    //    for (cnc_y = y0; cnc_y <= y1; cnc_y += dy)
                    for (cnc_y = y0; (cnc_y <= y1) && (iy < ny); cnc_y += dy, iy++)

                    {
                        timer2.Stop();
                        while (true)
                        {
                            Thread.Sleep(250);
                            Switch1.SendCmd("?" + (char)13 + (char)10);
                            Thread.Sleep(250);

                            string txt = Switch1.GetAcceptedKiril();
                            textBox27.AppendText(txt + "\n");
                            if ((txt.IndexOf("Idle") > -1) && (txt.IndexOf("WCO") == -1))
                            {
                                break;
                            }

                        }
                        timer2.Start();
                        sss = "g01";
                        if (checkBox16.Checked)
                        {
                            sss = String.Concat(sss, " x", cnc_x.ToString().Replace(',', '.'));

                        }
                        if (checkBox17.Checked)
                        {
                            sss = String.Concat(sss, " y", cnc_y.ToString().Replace(',', '.'));
                        }
                        sss = String.Concat(sss, " f", feed_rate.ToString().Replace(',', '.'));

                        cmd = sss + (char)13 + (char)10;
                        Thread.Sleep(250);
                        Switch1.SendCmd(cmd);
                        //       sss = String.Concat("px = ", cnc_x.ToString().Replace(',', '.'), " py = ", cnc_y.ToString().Replace(',', '.'));
                        label34.Text = cmd;
                        Thread.Sleep(250);
                        // Thread.Sleep(1000);
                        timer2.Stop();
                        while (true)
                        {
                            Switch1.SendCmd("?" + (char)13 + (char)10);
                            Thread.Sleep(25);

                            string txt = Switch1.GetAcceptedKiril();
                            textBox27.AppendText(txt + "\n");
                            if ((txt.IndexOf("Idle") > -1) && (txt.IndexOf("WCO") == -1))
                            {
                                break;
                            }

                        }
                        timer2.Start();
                        if (checkBox15.Checked)
                        {
                            string dir = textBox3.Text;
                            if (dir[dir.Length - 1] != '\\')
                            {
                                dir = String.Concat(dir, "\\");
                            }

                            Directory.CreateDirectory(dir);

                            int step_i = int.Parse(textBox19.Text);
                            timer1.Enabled = true;
                            CollectData();
                            for (uint i = 0; i < samples1 + samples2; i++)
                            {
                                arrA[i] = (double)masA[i] / 2.0 / (double)count_avg * inputRanges[comboRangeA.SelectedIndex] / 65536.0;
                                if (checkBox2.Checked)
                                {
                                    //      SuppressSpikes(arrA, int.Parse(textBox14.Text));
                                }
                            }

                            if (checkBox2.Checked)
                            {
                                SuppressSpikes(arrA, int.Parse(textBox14.Text));
                            }
                            bool _reason = checkBox2.Checked || checkBox3.Checked || checkBox4.Checked;
                            if (_reason)
                            {
                                if (checkBox2.Checked)
                                {
                                    SuppressSpikes(arrA, int.Parse(textBox14.Text));
                                }
                                if (checkBox3.Checked)
                                {
                                    RunAvg(ref arrA, int.Parse(textBox2.Text));
                                };
                                if (checkBox4.Checked)
                                {
                                    NoOffset(arrA);
                                };
                            }

                            if (checkBox8.Checked)
                            {
                                if (textBox15.Text.Length > 0)
                                {
                                    Complex[] filtr = WorkWithFiles.LoadFromFileC(textBox15.Text);
                                    arrA = FurieTrans.FuncMult(arrA, oscilloscope_timestep, -oscilloscope_timestep * (double)samples1, filtr);
                                }
                            }

                            if (checkBox4.Checked)
                            {
                                NoOffset(arrA);
                            };

                            if (checkBox9.Checked)
                            {
                                if (textBox16.Text.Length > 0)
                                {
                                    Complex[] f = FurieTrans.FurieTransf(arrA, oscilloscope_timestep, -oscilloscope_timestep * (double)samples1,
                                         double.Parse(textBox4.Text), double.Parse(textBox5.Text), int.Parse(textBox6.Text));
                                    Complex[] filtr = WorkWithFiles.LoadFromFileC(textBox16.Text);
                                    f = FurieTrans.FuncMult(f, double.Parse(textBox5.Text), double.Parse(textBox4.Text), filtr);
                                    Complex[] restored = FurieTrans.FurieTransfReverse(f, oscilloscope_timestep, -oscilloscope_timestep * (double)samples1, arrA.Length, double.Parse(textBox4.Text), double.Parse(textBox5.Text));
                                    for (int k1 = 0; k1 < restored.Length; k1++)
                                    {
                                        arrA[k1] = restored[k1].Real * 2;//важно делать умножение на 2 так как интеграл по полубесконечному промежутку
                                    }
                                }
                            }

                            timer1.Enabled = false;
                            sss = String.Concat("px", cnc_x.ToString(), "py", cnc_y.ToString(), ".txt");

                            string fulldir = String.Concat(dir, sss);
                            Action a = () => WorkWithFiles.Save2FileAsync(fulldir, arrA);
                            // Task.Factory.StartNew(a);
                            Task.Run(a);
                            //  Save2File(fulldir, arrA);
                            if (stop_flag)
                            {
                                save = 0; stop_flag = false;
                            }

                            //  Visualase(Color.Red, arrA, 5);

                        }

                    }
                    Thread.Sleep(5);

                }
            }
        }
        private void button10_Click(object sender, EventArgs e)
        {
            NoOffset(arrA);
            string path = String.Concat(textBox3.Text, DateTime.Now.ToString().Replace(':', '_'), "TempCapture.txt");
            WorkWithFiles.Save2File(path, arrA);
        }

        // ──────────────────────────────────────────────────────────────────────
        // Wave broker integration
        // ──────────────────────────────────────────────────────────────────────

        private void InitBrokerControls()
        {
            var grp = new System.Windows.Forms.GroupBox
            {
                Text = "Публикация в брокер wave-mq",
                Location = new System.Drawing.Point(10, 10),
                Size = new System.Drawing.Size(660, 200),
                Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right,
            };

            _checkBoxBroker = new System.Windows.Forms.CheckBox
            {
                Text = "Включить публикацию",
                Location = new System.Drawing.Point(10, 25),
                AutoSize = true,
            };
            _checkBoxBroker.CheckedChanged += (s, e) =>
            {
                BrokerPublisher.Enabled = _checkBoxBroker.Checked;
            };

            var lblUrl = new System.Windows.Forms.Label
            {
                Text = "Адрес брокера:",
                Location = new System.Drawing.Point(10, 60),
                AutoSize = true,
            };

            _textBoxBrokerUrl = new System.Windows.Forms.TextBox
            {
                Text = "http://localhost:8090",
                Location = new System.Drawing.Point(10, 80),
                Width = 280,
            };
            _textBoxBrokerUrl.TextChanged += (s, e) =>
            {
                BrokerPublisher.BrokerUrl = _textBoxBrokerUrl.Text.Trim();
            };

            var btnCheck = new System.Windows.Forms.Button
            {
                Text = "Проверить",
                Location = new System.Drawing.Point(300, 78),
                Width = 110,
            };
            btnCheck.Click += async (s, e) =>
            {
                BrokerPublisher.BrokerUrl = _textBoxBrokerUrl.Text.Trim();
                bool ok = await BrokerPublisher.CheckTopicExistsAsync();
                _labelBrokerStatus.Text = ok ? "Брокер: ОК" : "Брокер: нет топика";
                _labelBrokerStatus.ForeColor = ok ? System.Drawing.Color.Green : System.Drawing.Color.Red;
            };

            _labelBrokerStatus = new System.Windows.Forms.Label
            {
                Text = "Брокер: не проверен",
                Location = new System.Drawing.Point(10, 115),
                AutoSize = true,
                ForeColor = System.Drawing.Color.Gray,
            };

            _buttonContinuous = new System.Windows.Forms.Button
            {
                Text = "Непрерывная публикация",
                Location = new System.Drawing.Point(10, 145),
                Width = 220,
                Height = 32,
            };
            _buttonContinuous.Click += buttonContinuous_Click;

            grp.Controls.Add(_checkBoxBroker);
            grp.Controls.Add(lblUrl);
            grp.Controls.Add(_textBoxBrokerUrl);
            grp.Controls.Add(btnCheck);
            grp.Controls.Add(_labelBrokerStatus);
            grp.Controls.Add(_buttonContinuous);

            var tabPublish = new System.Windows.Forms.TabPage
            {
                Text = "Публикация",
                Name = "tabPagePublish",
            };
            tabPublish.Controls.Add(grp);

            tabControl1.TabPages.Add(tabPublish);
        }

        private void PublishCapture(double[] data)
        {
            if (!BrokerPublisher.Enabled) return;
            int n = data.Length;
            var samples = new float[n];
            for (int i = 0; i < n; i++) samples[i] = (float)data[i];
            long tsNs = (long)(DateTimeOffset.UtcNow - new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero)).TotalMilliseconds * 1000000L;
            byte[] frame = BrokerPublisher.EncodeFrame(tsNs, _currentSampleRateHz, n, 0, 2, samples);
            BrokerPublisher.PublishAsync(frame);
        }

        private void buttonContinuous_Click(object sender, EventArgs e)
        {
            if (_continuousCts != null)
            {
                _continuousCts.Cancel();
                _continuousCts = null;
                _buttonContinuous.Text = "Непрерывная публикация";
                return;
            }

            // Read all UI state on UI thread before entering background task
            uint samples1, samples2, tb;
            Imports.Range range;
            bool bwFilter;
            ushort rangeMilliV;
            try
            {
                samples1 = uint.Parse(textBox13.Text);
                samples2 = uint.Parse(textBox10.Text);
                tb = uint.Parse(textBox9.Text);
                range = (Imports.Range)comboRangeA.SelectedIndex;
                bwFilter = checkBox1.Checked;
                rangeMilliV = inputRanges[comboRangeA.SelectedIndex];
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("Не удалось прочитать параметры: " + ex.Message);
                return;
            }

            if (_handle == 0)
            {
                System.Windows.Forms.MessageBox.Show("Осциллограф не подключён. Откройте устройство (кнопка Open).");
                return;
            }

            _continuousCts = new CancellationTokenSource();
            var cts = _continuousCts;
            _buttonContinuous.Text = "Стоп";

            Task.Run(() =>
            {
                double mult = 1.0 / 2.0 * rangeMilliV / 65536.0;
                uint total = samples1 + samples2;

                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        long[] localMas;
                        lock (_captureLock)
                        {
                            masA = new long[total];
                            start(samples1, samples2, 1, range, bwFilter, tb);
                            localMas = masA;
                        }

                        var arr = new double[total];
                        for (uint j = 0; j < total; j++)
                            arr[j] = localMas[j] * mult;

                        PublishCapture(arr);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[Continuous] " + ex.Message);
                        System.Threading.Thread.Sleep(500);
                    }

                    System.Threading.Thread.Sleep(10);
                }

                this.Invoke((Action)(() =>
                {
                    _buttonContinuous.Text = "Непрерывная публикация";
                    _continuousCts = null;
                }));
            }, cts.Token);
        }

    }
}