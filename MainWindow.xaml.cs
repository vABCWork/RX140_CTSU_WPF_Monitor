using Microsoft.Win32;
using ScottPlot;
using ScottPlot.Palettes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace CommTest
{

    // COMポートの　コンボボックス用 
    public class ComPortNameClass
    {
        string _ComPortName;

        public string ComPortName
        {
            get { return _ComPortName; }
            set { _ComPortName = value; }
        }
    }


    // 履歴(ヒストリ)データ　クラス
    // クラス名: HistoryData
    // メンバー:  double  data0
    //            double  data1
    //            double  data2
    //            double  data3
    //            double  dt
    //

    public class HistoryData
    {
        public double data0 { get; set; }       // ch0のデータ　
        public double data1 { get; set; }       // ch1のデータ
        public double data2 { get; set; }       // ch2のデータ
        public double data3 { get; set; }       // ch3のデータ
        public double dt { get; set; }         // 日時 (double型)
    }


    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        UInt16 ch0_rd_data;       // ch0 読み出しデータ 
        UInt16 ch1_rd_data;       // ch1
        UInt16 ch2_rd_data;       // ch2
        UInt16 ch3_rd_data;       // ch3

        UInt16 pre_ch0_rd_data;  // 前回の ch0 読み出しデータ


        double ch0_data;          // ch0
        double ch1_data;          // ch1
        double ch2_data;          // 未使用 ch2 (AIN2+)
        double ch3_data;          // 温度 サーミスタ　ch3 (AIN3+) 

        uint trend_data_item_max;             // 各リアルタイム　トレンドデータの保持数 

        double[] trend_data0;                 // トレンドデータ 0 
        double[] trend_data1;                 // トレンドデータ 1              
        double[] trend_data2;                 // トレンドデータ 2  
        double[] trend_data3;                 // トレンドデータ 3 

        double[] trend_dt;                    // トレンドデータ　収集日時

        ScottPlot.Plottables.Scatter trend_scatter_0; // トレンドデータ0  
        ScottPlot.Plottables.Scatter trend_scatter_1; // トレンドデータ1  
        ScottPlot.Plottables.Scatter trend_scatter_2; // トレンドデータ2  
        ScottPlot.Plottables.Scatter trend_scatter_3; // トレンドデータ3  


        public List<HistoryData> historyData_list;          // ヒストリデータ　データ収集時に使用


        double y_axis_top;                      // Y軸 温度目盛りの上限値
        double y_axis_bottom;                   // Y軸 温度目盛りの下限値

        public static DispatcherTimer SendIntervalTimer;  // タイマ　モニタ用　電文送信間隔   
        DispatcherTimer RcvWaitTimer;                   　// タイマ　受信待ち用 

        public ObservableCollection<ComPortNameClass> ComPortNames;    // 通信ポート(COM1,COM2等)のコレクション 
                                                                       // データバインドするため、ObservableCollection　
        public static SerialPort serialPort;        // シリアルポート

        public static byte[] sendBuf;          // 送信バッファ   
        int sendByteLen;         //　送信データのバイト数

        byte[] rcvBuf;           // 受信バッファ
        int srcv_pt;             // 受信データ格納位置

        DateTime sendDateTime;   // 送信日時
        DateTime receiveDateTime;   // 受信完了日時

        string dot_net_ver;
        public MainWindow()
        {
            InitializeComponent();

            MainWindow.serialPort = new SerialPort();    // シリアルポートのインスタンス生成
            MainWindow.serialPort.DataReceived += new SerialDataReceivedEventHandler(DataReceivedHandler);  // データ受信時のイベント処理

            ComPortNames = new ObservableCollection<ComPortNameClass>();  // 通信ポートのコレクション　インスタンス生成

            ComPortComboBox.ItemsSource = ComPortNames;       // 通信ポートコンボボックスのアイテムソース指定  

            SetComPortName();                // 通信ポート名をコンボボックスへ設定

            sendBuf = new byte[16];     // 送信バッファ領域  
            rcvBuf = new byte[64];      // 受信バッファ領域   


            SendIntervalTimer = new System.Windows.Threading.DispatcherTimer();　　// タイマーの生成(定周期モニタ用)
            SendIntervalTimer.Tick += new EventHandler(SendIntervalTimer_Tick);  // タイマーイベント
            SendIntervalTimer.Interval = new TimeSpan(0, 0, 0, 0, 1000);         // タイマーイベント発生間隔 1sec(コマンド送信周期)

            RcvWaitTimer = new System.Windows.Threading.DispatcherTimer();　 // タイマーの生成(受信待ちタイマ)
            RcvWaitTimer.Tick += new EventHandler(RcvWaitTimer_Tick);        // タイマーイベント
            RcvWaitTimer.Interval = new TimeSpan(0, 0, 0, 0, 1000);          // タイマーイベント発生間隔 (受信待ち時間)

            historyData_list = new List<HistoryData>();     // モニタ時のトレンドデータ 記録用　


            Loaded += LoadEvent;      // LoadEvent実行

        }

        //
        // 要素のレイアウトやレンダリングが完了し、操作を受け入れる準備が整ったときに発生
        //
        private void LoadEvent(object sender, EventArgs e)
        {
          
            Chart_Ini();    // チャートの初期表示
        }

        //
        // モニタ開始ボタン
        //
        private void Start_Monitor_Button_Click(object sender, RoutedEventArgs e)
        {
            CTSU_read_cmd();         // SLC47011 測定温度 読み出しコマンドの作成

            SendIntervalTimer.Start();   // 定周期　送信用タイマの開始
        }


        // Test Sendボタンを押した時の処理
        // データの送信
        private void Test_Send_Button_Click(object sender, RoutedEventArgs e)
        {
            CTSU_read_cmd();         // SLC47011 測定温度 読み出しコマンドの作成


            if (serialPort.IsOpen == true)
            {
                srcv_pt = 0;                   // 受信データ格納位置クリア

                serialPort.Write(sendBuf, 0, sendByteLen);     // データ送信

                sendDateTime = DateTime.Now;    // 送信日時

                SendTextBox.Text = get_send_str();// 送信データの文字列表示

                RcvWaitTimer.Start();  // 受信監視タイマー　開始
            }
            else
            {
                disp_msg_com_port_closed();     // COM　Port Closedのメッセージボックスの表示
            }

        }

        //
        // 送信データの文字列を得る
        //
        private string get_send_str()
        {
            string send_str = "";

            for (int i = 0; i < sendByteLen; i++)   // 表示用の文字列作成
            {
                if ((i > 0) && (i % 16 == 0))    // 16バイト毎に1行空ける
                {
                    send_str = send_str + "\r\n";
                }

                send_str = send_str + sendBuf[i].ToString("X2") + " ";
            }

            send_str = send_str + "(" + sendDateTime.ToString("HH:mm:ss,fff") + ")";   // 受信データ文字列

            return send_str;
        }


        //
        //  CTSU タッチセンサカウント値 読み出しコマンドの作成
        // 
        //  sendBuf[0]  0x10 :パラメータ書き込みコマンド 
        //         [1]  dummy data 0x00
        //         [2]  dummy data 0x00
        //         [3]  dummy data 0x00
        //         [4]  dummy data 0x00
        //         [5]  dummy data 0x00
        //         [6]  CRC 上位バイト
        //         [7]  CRC 下位バイト
        private void CTSU_read_cmd()
        {
            UInt16 crc_cd;
            
            sendBuf[0] = 0x10;     // 送信コマンド  
            sendBuf[1] = 0x00;
            sendBuf[2] = 0x00;
            sendBuf[3] = 0x00;
            sendBuf[4] = 0x00;
            sendBuf[5] = 0x00;

            crc_cd = CRC_sendBuf_Cal(6);     // CRC計算

            sendBuf[6] = (Byte)(crc_cd >> 8); // CRCは上位バイト、下位バイトの順に送信
            sendBuf[7] = (Byte)(crc_cd & 0x00ff);
                        
            sendByteLen = 8;               // 送信バイト数

        }


        // CRCの計算 (送信バッファ用)
        //  CRC-16 CCITT:
        //  多項式: X^16 + X^12 + X^5 + 1　
        //  初期値: 0xffff
        //  MSBファースト
        //  非反転出力
        // 
        public static UInt16 CRC_sendBuf_Cal(UInt16 size)
        {
            UInt16 crc;

            UInt16 i;

            crc = 0xffff;

            for (i = 0; i < size; i++)
            {
                crc = (UInt16)((crc >> 8) | ((UInt16)((UInt32)crc << 8)));
                crc = (UInt16)(crc ^ (sendBuf[i]));
                crc = (UInt16)(crc ^ (UInt16)((crc & 0xff) >> 4));
                crc = (UInt16)(crc ^ (UInt16)((crc << 8) << 4));
                crc = (UInt16)(crc ^ (((crc & 0xff) << 4) << 1));
            }

            return crc;

        }

        // CRCの計算 (受信バッファ用)
        //  CRC-16 CCITT:
        //  多項式: X^16 + X^12 + X^5 + 1　
        //  初期値: 0xffff
        //  MSBファースト
        //  非反転出力
        // 
        private UInt16 CRC_rcvBuf_Cal(UInt16 size)
        {
            UInt16 crc;

            UInt16 i;

            crc = 0xffff;

            for (i = 0; i < size; i++)
            {
                crc = (UInt16)((crc >> 8) | ((UInt16)((UInt32)crc << 8)));

                crc = (UInt16)(crc ^ rcvBuf[i]);
                crc = (UInt16)(crc ^ (UInt16)((crc & 0xff) >> 4));
                crc = (UInt16)(crc ^ (UInt16)((crc << 8) << 4));
                crc = (UInt16)(crc ^ (((crc & 0xff) << 4) << 1));
            }

            return crc;

        }

        // 定周期モニタ用
        // 
        private void SendIntervalTimer_Tick(object sender, EventArgs e)
        {
            if (serialPort.IsOpen == true)
            {
                srcv_pt = 0;                   // 受信データ格納位置クリア

                serialPort.Write(sendBuf, 0, sendByteLen);     // データ送信
                
                sendDateTime = DateTime.Now;    // 送信日時

                SendTextBox.Text = get_send_str();// 送信データの文字列表示

                RcvWaitTimer.Start();        // 受信監視タイマー　開始
            }

            else
            {
                disp_msg_com_port_closed();     // COM　Port Closedのメッセージボックスの表示

                SendIntervalTimer.Stop();
            }
        }

        //
        // COM　Port Closedのメッセージボックスの表示
        private void disp_msg_com_port_closed()
        {
            var msg = " COM Port Closed. \r\n";

            MessageBox.Show(msg, "Warning", MessageBoxButton.OK, MessageBoxImage.Warning); // メッセージボックスの表示
        }

        // モニタ停止ボタン
        private void Stop_Monitor_Button_Click(object sender, RoutedEventArgs e)
        {
            SendIntervalTimer.Stop();     // データ収集用コマンド送信タイマー停止
        }



        //
        // 送信後、1000msec以内に受信文が得られないと、受信エラー
        //  
        private void RcvWaitTimer_Tick(object sender, EventArgs e)
        {
            RcvWaitTimer.Stop();        // 受信監視タイマの停止
            SendIntervalTimer.Stop();   // 定周期モニタ用タイマの停止

            var msg = "Receive time out. \r\n";

            MessageBox.Show(msg, "Warning", MessageBoxButton.OK, MessageBoxImage.Warning); // メッセージボックスの表示

        }


        // デリゲート関数の宣言
        private delegate void DelegateFn();

        // データ受信時のイベント処理
        private void DataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
        {

            int rd_num = MainWindow.serialPort.BytesToRead;       // 受信データ数

            MainWindow.serialPort.Read(rcvBuf, srcv_pt, rd_num);   // 受信データを読み出して、受信バッファに格納

            srcv_pt = srcv_pt + rd_num;     // 次回の保存位置


            if (srcv_pt == 24 )  // 最終データ受信済み (CTSU読み出しコマンドのレスポンス 受信データ数 = 24 byte)　イベント処理の終了
            {
                RcvWaitTimer.Stop();        // 受信監視タイマの停止

                Dispatcher.BeginInvoke(new DelegateFn(RcvProc)); // Delegateを生成して、RcvProcを開始   (表示は別スレッドのため)
            }

        }

        //
        // データ受信イベント終了時の処理
        // 受信データの表示
        //
        private void RcvProc()
        {
            RcvTextBox.Text = get_rcv_str();  　   // 受信データ表示

            Disp_monitor_data();   //  モニタ表示とグラフ表示

        }

        //
        // 受信データの文字列を得る
        //
        private string get_rcv_str()
        {
            string rcv_str = "";

            for (int i = 0; i < srcv_pt; i++)   // 表示用の文字列作成
            {
                if ((i > 0) && (i % 16 == 0))    // 16バイト毎に1行空ける
                {
                    rcv_str = rcv_str + "\r\n";
                }

                rcv_str = rcv_str + rcvBuf[i].ToString("X2") + " ";
            }

            receiveDateTime = DateTime.Now;   // 受信完了時刻を得る

            rcv_str = rcv_str + "(" + receiveDateTime.ToString("HH:mm:ss.fff") + ")";   // 受信データ文字列

            return rcv_str;
        }


        // モニタ表示とグラフ表示
        //   受信データ :内容
        //     rcvBuf[0] : 0x90 (レスポンス) 
        //     rcvBuf[1] : dummy 0    
        //     rcvBuf[2] : TS14用 CTSUステータスレジスタ ctsu_sr(bit0-bit7) 
        //     rcvBuf[3] :             :                        (bit8-bit15)
        //     rcvBuf[4] : TS15用 CTSUステータスレジスタ ctsu_sr(bit0-bit7) 
        //     rcvBuf[5] :             :                        (bit8-bit15)
        //     rcvBuf[6] : TS14用 CTSUセンサオフセットレジスタ ctsu_so (bit0-bit7) 
        //     rcvBuf[7] :             :                                (bit8-bit15)
        //     rcvBuf[8] :             :                                (bit16-bit23)
        //     rcvBuf[9] :             :                                (bit17-bit31)
        //     rcvBuf[10] : TS15用 CTSUセンサオフセットレジスタ ctsu_so(bit0-bit7)
        //     rcvBuf[11] :             :                                (bit8-bit15)
        //     rcvBuf[12] :             :                                (bit16-bit23)
        //     rcvBuf[13] :             :                                (bit17-bit31)
        //     rcvBuf[14] :TS14用 CTSUセンサカウンタ ctsu_scnt (bit0-bit7)
        //     rcvBuf[15] :             :                      (bit8-bit15)
        //     rcvBuf[16] :                                    (bit16-bit23)
        //     rcvBuf[17] :             :                      (bit24-bit31)
        //     rcvBuf[18] :TS15用 CTSUセンサカウンタ ctsu_scnt (bit0-bit7)
        //     rcvBuf[19] :             :                      (bit8-bit15)
        //     rcvBuf[20] :                                    (bit16-bit23)
        //     rcvBuf[21] :             :                      (bit24-bit31)
        //     rcvBuf[22] : CRC 上位バイト 
        //     rcvBuf[23] : CRC 下位バイト
        private void Disp_monitor_data()
        {

            UInt16 crc_cd = CRC_rcvBuf_Cal(24);         // 全データのCRC計算             

            if (crc_cd != 0)
            {
                AlarmTextBox.Text = "Receive data CRC Err.";
                return;
            }
            else
            {
                AlarmTextBox.Text = "";
            }

            UInt16 ts14_ctsu = BitConverter.ToUInt16(rcvBuf, 2);
            UInt16 ts15_ctsu = BitConverter.ToUInt16(rcvBuf, 4);

            CTSUSR_TS14_TextBox.Text = "0x" + ts14_ctsu.ToString("x4");     // CTSUステータスレジスタ(TS14用)
            CTSUSR_TS15_TextBox.Text = "0x" + ts15_ctsu.ToString("x4");


            ch0_data = BitConverter.ToUInt16(rcvBuf, 14);    // TS14 センサカウント値
            UInt16 ts14_cnt = (UInt16) ch0_data;

            ch1_data = BitConverter.ToUInt16(rcvBuf, 18);    // TS15センサカウント
            UInt16 ts15_cnt = (UInt16) ch1_data;

            ch2_data = 0;               // 未使用
            ch3_data = 0;               // 未使用

            Ch0_TextBox.Text = ch0_data.ToString();
            Ch1_TextBox.Text = ch1_data.ToString();
            Ch2_TextBox.Text = "---";
            Ch3_TextBox.Text = "---";

          
            Ch0_TextBox_Hex.Text =  "0x" + ts14_cnt.ToString("x4");
            Ch1_TextBox_Hex.Text = "0x" + ts15_cnt.ToString("x4");


            Store_History();                // ヒストリデータとして保持

            Chart_update();                 // チャートの更新

        }

        //
        //  ヒストリデータとして保持
        //
        private void Store_History()
        {

            HistoryData historyData = new HistoryData();     // 保存用ヒストリデータ

            historyData.data0 = ch0_data;
            historyData.data1 = ch1_data;
            historyData.data2 = ch2_data;
            historyData.data3 = ch3_data;

            historyData.dt = receiveDateTime.ToOADate();   // 受信日時を deouble型で格納

            historyData_list.Add(historyData);          // Listへ保持

        }


        //
        //   チャートの更新
        private void Chart_update()
        {

            // 1スキャン前のデータを移動後、最新のデータを入れる
            Array.Copy(trend_data0, 1, trend_data0, 0, trend_data_item_max - 1);
            trend_data0[trend_data_item_max - 1] = ch0_data;

            Array.Copy(trend_data1, 1, trend_data1, 0, trend_data_item_max - 1);
            trend_data1[trend_data_item_max - 1] = ch1_data;

            Array.Copy(trend_data2, 1, trend_data2, 0, trend_data_item_max - 1);
            trend_data2[trend_data_item_max - 1] = ch2_data;

            Array.Copy(trend_data3, 1, trend_data3, 0, trend_data_item_max - 1);
            trend_data3[trend_data_item_max - 1] = ch3_data;


            Array.Copy(trend_dt, 1, trend_dt, 0, trend_data_item_max - 1);
            trend_dt[trend_data_item_max - 1] = receiveDateTime.ToOADate();    // 受信日時 double型に変換して、格納


            Axis_make();            // 軸の作成

            wpfPlot_Trend.Refresh();   // リアルタイム グラフの更新


        }


        //
        // 　チャートの初期化(リアルタイム　チャート用)
        //
        private void Chart_Ini()
        {
            trend_data_item_max = 30;             // 各リアルタイム　トレンドデータの保持数(=30 ) 1秒毎に収集すると、30秒分のデータ

            trend_data0 = new double[trend_data_item_max];
            trend_data1 = new double[trend_data_item_max];
            trend_data2 = new double[trend_data_item_max];
            trend_data3 = new double[trend_data_item_max];

            trend_dt = new double[trend_data_item_max];

            DateTime datetime = DateTime.Now;   // 現在の日時

            DateTime[] myDates = new DateTime[trend_data_item_max];  // 日時型



            for (int i = 0; i < trend_data_item_max; i++)  // 初期値の設定
            {
                trend_data0[i] = 30000 + (i * 1000);
                trend_data1[i] = 20000 + (i * 1000);
                trend_data2[i] = 10000 + (i * 1000);
                trend_data3[i] = 5000 + (i * 1000);

                myDates[i] = datetime + new TimeSpan(0, 0, i);  // i秒増やす

                trend_dt[i] = myDates[i].ToOADate();   // (現在の日時 + i 秒)をdouble型に変換
            }


            trend_scatter_0 = wpfPlot_Trend.Plot.Add.Scatter(trend_dt, trend_data0, ScottPlot.Colors.Blue); // プロット plot the data array only once
            trend_scatter_1 = wpfPlot_Trend.Plot.Add.Scatter(trend_dt, trend_data1, ScottPlot.Colors.Orange);
            trend_scatter_2 = wpfPlot_Trend.Plot.Add.Scatter(trend_dt, trend_data2, ScottPlot.Colors.Gainsboro);
            trend_scatter_3 = wpfPlot_Trend.Plot.Add.Scatter(trend_dt, trend_data3, ScottPlot.Colors.Green);

            wpfPlot_Trend.UserInputProcessor.IsEnabled = false;     // マウスによるパン(グラフの移動)、ズーム(グラフの拡大、縮小)の操作禁止

            Axis_make();            // 軸の作成

            wpfPlot_Trend.Plot.Axes.Left.Label.FontSize = 20;           // Y軸(左側) ラベルのフォントサイズ変更  :
            wpfPlot_Trend.Plot.Axes.Left.Label.Text = " ";          // Y軸(左側) のラベル (scottplot.net/cookbook/5.0/Styling/AxisCustom/)

            // 凡例の表示
            // 参考:scottplot.net/cookbook/5.0/Legend/
            //
            wpfPlot_Trend.Plot.Legend.FontSize = 24;

            trend_scatter_0.LegendText = "TS14";
            trend_scatter_1.LegendText = "TS15";
            trend_scatter_2.LegendText = "ch2";
            trend_scatter_3.LegendText = "ch3";

            wpfPlot_Trend.Plot.ShowLegend(Alignment.UpperLeft, ScottPlot.Orientation.Vertical);

            wpfPlot_Trend.Refresh();        // データ変更後のリフレッシュ
        }


        //
        // 軸の作成
        //
        private void Axis_make()
        {
            y_axis_top = 65535;                     // Y軸　上限値
            y_axis_bottom = 0;                      // Y軸　下限値

            // X軸の日時リミットを、最終日時+1秒にする
            DateTime dt_end = DateTime.FromOADate(trend_dt[trend_data_item_max - 1]); // double型を　DateTime型に変換
            TimeSpan dt_sec = new TimeSpan(0, 0, 1);    // 1 秒
            DateTime dt_limit = dt_end + dt_sec;      // DateTime型(最終日時+ 1秒) 
            double dt_ax_limt = dt_limit.ToOADate();   // double型(最終日時+ 1秒) 

            wpfPlot_Trend.Plot.Axes.SetLimits(trend_dt[0], dt_ax_limt, y_axis_bottom, y_axis_top);  // X軸の最小=現在の時間 ,X軸の最大=最終日時+1秒,Y軸下限=0, Y軸上限=2000

            custom_ticks();                             // X軸の目盛りのカスタマイズ

        }

        //
        //  目盛りのカスタマイズ 
        // 参考: scottplot.net/cookbook/5.0/CustomizingTicks/
        //
        //       Custom Tick DateTimes
        // Users may define custom ticks using DateTime units
        // 
        private void custom_ticks()
        {
            DateTime dt;
            string label;

            // create a manual DateTime tick generator and add ticks
            ScottPlot.TickGenerators.DateTimeManual ticks = new ScottPlot.TickGenerators.DateTimeManual();

            //for (int i = 0; i < trend_data_item_max; i++)  // 1秒毎に目盛りのラベル表示
            //{
            //    DateTime dt = DateTime.FromOADate(trend_dt[i]);
            //    string label = dt.ToString("HH:mm:ss");
            //    ticks.AddMajor(dt, label);
            //}


            dt = DateTime.FromOADate(trend_dt[1]);  // 先頭 + 1の時刻　目盛りのラベル表示
            label = dt.ToString("HH:mm:ss");
            ticks.AddMajor(dt, label);

            UInt16 t = (ushort)(trend_data_item_max / 2);
            dt = DateTime.FromOADate(trend_dt[t]);  // 中間の時刻　目盛りのラベル表示
            label = dt.ToString("HH:mm:ss");
            ticks.AddMajor(dt, label);

            dt = DateTime.FromOADate(trend_dt[trend_data_item_max - 1]);  // 最後の時刻　目盛りのラベル表示
            label = dt.ToString("HH:mm:ss");
            ticks.AddMajor(dt, label);

            wpfPlot_Trend.Plot.Axes.Bottom.TickGenerator = ticks;    　　　　// tell the horizontal axis to use the custom tick generator

            wpfPlot_Trend.Plot.Axes.Bottom.TickLabelStyle.FontSize = 24;      //  X軸　目盛りのフォントサイズ


            wpfPlot_Trend.Plot.Axes.Left.TickLabelStyle.FontSize = 24;        //  Y軸(左側)　目盛りのフォントサイズ
            wpfPlot_Trend.Plot.Axes.Right.TickLabelStyle.FontSize = 24;       //  Y軸(右側)  目盛りのフォントサイズ
        }




        // チェックボックスによるトレンド線の表示 
        private void CH_N_Show(object sender, RoutedEventArgs e)
        {

            if (trend_scatter_0 is null) return;
            if (trend_scatter_1 is null) return;
            if (trend_scatter_2 is null) return;
            if (trend_scatter_3 is null) return;

            CheckBox checkBox = (CheckBox)sender;

            if (checkBox.Name == "Ch0_CheckBox")
            {
                trend_scatter_0.IsVisible = true;
            }
            else if (checkBox.Name == "Ch1_CheckBox")
            {
                trend_scatter_1.IsVisible = true;
            }
            else if (checkBox.Name == "Ch2_CheckBox")
            {
                trend_scatter_2.IsVisible = true;
            }
            else if (checkBox.Name == "Ch3_CheckBox")
            {
                trend_scatter_3.IsVisible = true;
            }


            wpfPlot_Trend.Refresh();   // グラフの更新

        }

        // チェックボックスによるトレンド線の非表示
        private void CH_N_Hide(object sender, RoutedEventArgs e)
        {
            if (trend_scatter_0 is null) return;
            if (trend_scatter_1 is null) return;
            if (trend_scatter_2 is null) return;
            if (trend_scatter_3 is null) return;

            CheckBox checkBox = (CheckBox)sender;

            if (checkBox.Name == "Ch0_CheckBox")
            {
                trend_scatter_0.IsVisible = false;
            }
            else if (checkBox.Name == "Ch1_CheckBox")
            {
                trend_scatter_1.IsVisible = false;
            }
            else if (checkBox.Name == "Ch2_CheckBox")
            {
                trend_scatter_2.IsVisible = false;
            }
            else if (checkBox.Name == "Ch3_CheckBox")
            {
                trend_scatter_3.IsVisible = false;
            }

            wpfPlot_Trend.Refresh();   // グラフの更新
        }



        // 保持しているデータをファイルへ保存
        private void Save_Button_Click(object sender, RoutedEventArgs e)
        {
            string path;

            string str_one_line;

            SaveFileDialog sfd = new SaveFileDialog();           //　SaveFileDialogクラスのインスタンスを作成 

            sfd.FileName = "temp_trend.csv";                              //「ファイル名」で表示される文字列を指定する

            sfd.Title = "保存先のファイルを選択してください。";        //タイトルを設定する 

            sfd.RestoreDirectory = true;                 //ダイアログボックスを閉じる前に現在のディレクトリを復元するようにする

            if (sfd.ShowDialog() == true)            //ダイアログを表示する
            {
                path = sfd.FileName;

                try
                {
                    System.IO.StreamWriter sw = new System.IO.StreamWriter(path, false, System.Text.Encoding.Default);

                    str_one_line = DataMemoTextBox.Text; // メモ欄
                    sw.WriteLine(str_one_line);         // 1行保存


                    str_one_line = "DateTime" + "," + "TS14" + "," + "TS15" + "," + "ch2(invalid)" + "," + "ch3(invalid)";
                    sw.WriteLine(str_one_line);         // 1行保存


                    foreach (HistoryData historyData in historyData_list)         // historyData_listの内容を保存
                    {
                        DateTime dateTime = DateTime.FromOADate(historyData.dt); // 記録されている日時(double型)を　DateTime型に変換

                        string st_dateTime = dateTime.ToString("yyyy/MM/dd HH:mm:ss.fff");             // DateTime型を文字型に変換　（2021/10/22 11:09:06.125 )

                        string st_dt0 = historyData.data0.ToString();       //
                        string st_dt1 = historyData.data1.ToString();       // 
                        string st_dt2 = historyData.data2.ToString();       // 
                        string st_dt3 = historyData.data3.ToString();       // 


                        str_one_line = st_dateTime + "," + st_dt0 + "," + st_dt1 + "," + st_dt2 + "," + st_dt3;

                        sw.WriteLine(str_one_line);         // 1行保存
                    }

                    sw.Close();
                }

                catch (System.Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }

            }
        }


        // 収集済みのデータをクリアの確認
        private void Clear_Button_Click(object sender, RoutedEventArgs e)
        {
            string messageBoxText = "収集済みのデータがクリアされます。";
            string caption = "Check clear";

            MessageBoxButton button = MessageBoxButton.YesNoCancel;
            MessageBoxImage icon = MessageBoxImage.Warning;
            MessageBoxResult result;

            result = MessageBox.Show(messageBoxText, caption, button, icon, MessageBoxResult.Yes);

            switch (result)
            {
                case MessageBoxResult.Yes:      // Yesを押した場合
                    historyData_list.Clear();   // 収集済みのデータのクリア
                    break;

                case MessageBoxResult.No:
                    break;

                case MessageBoxResult.Cancel:
                    break;
            }
        }

        // トレンド 履歴画面
        private void History_Button_Click(object sender, RoutedEventArgs e)
        {
            var window = new HistoryWindow();      // 注意メッセージのダイアログを開く
            window.Owner = this;
            window.Show();
        }




        // 通信ポート名をコンボボックスへ設定
        private void SetComPortName()
        {
            ComPortNames.Clear();           // 通信ポートのコレクション　クリア

            string[] PortList = SerialPort.GetPortNames();              // 存在するシリアルポート名が配列の要素として得られる。

            foreach (string PortName in PortList)
            {
                ComPortNames.Add(new ComPortNameClass { ComPortName = PortName }); // シリアルポート名の配列を、コレクションへコピー
            }

            if (ComPortNames.Count > 0)
            {
                ComPortComboBox.SelectedIndex = 0;   // 最初のポートを選択
                ComPortOpenButton.IsEnabled = true;  // ポートOPENボタンを「有効」にする。

                //OpenInfoTextBox.Text = "(" + serialPort.PortName + ") is opened.";
                //ComPortOpenButton.Content = "Close";      // ボタン表示 Close

            }
            else
            {
                ComPortOpenButton.IsEnabled = false;  // ポートOPENボタンを「無効」にする。
                OpenInfoTextBox.Text = "COM port is not found.";
            }


        }


        // Findボタンを押した時の処理
        // 通信ポートの検索ボタン
        //
        private void ComPortSearchButton_Click(object sender, RoutedEventArgs e)
        {
            SetComPortName();
        }


        // Openボタンを押した時の処理
        // 通信ポートのオープン
        //
        //  SerialPort.ReadBufferSize = 4096 byte (デフォルト)
        //             WriteBufferSize =2048 byte
        //
        private void ComPortOpenButton_Click(object sender, RoutedEventArgs e)
        {
            if (serialPort.IsOpen == true)    // 既に Openしている場合
            {
                try
                {
                    serialPort.Close();

                    OpenInfoTextBox.Text = "Close(" + serialPort.PortName + ")";

                    ComPortComboBox.IsEnabled = true;        // 通信条件等を選択できるようにする。
                   // ComPortSearchButton.IsEnabled = true;    // 通信ポート検索ボタンを有効とする。
                    ComPortOpenButton.Content = "Open"; 　　 // ボタン表示を Closeから Openへ

                }
                catch (Exception ex)
                {
                    OpenInfoTextBox.Text = ex.Message;
                }

            }
            else                      // Close状態からOpenする場合
            {
                serialPort.PortName = ComPortComboBox.Text;    // 選択したシリアルポート

               
              //  serialPort.BaudRate = 750000;           // ボーレート 750[Kbps]

                  serialPort.BaudRate = 1500000;           // ボーレート 1.5[Mbps]

                //serialPort.BaudRate = 3000000;           // ボーレート 3[Mbps]

               // serialPort.BaudRate = 4000000;           // ボーレート 4[Mbps]

                //serialPort.BaudRate = 6000000;           // ボーレート 6[Mbps]

                //serialPort.BaudRate = 8000000;           // ボーレート 8[Mbps]

                //serialPort.BaudRate = 9000000;           // ボーレート 9[Mbps]

                //serialPort.BaudRate = 10000000;           // ボーレート 10[Mbps] 実測は 9[Mbps]となる。


                BaudrateTextBox.Text = serialPort.BaudRate.ToString();  // ボーレート表示


                serialPort.Parity = Parity.None;       // パリティ無し
                serialPort.StopBits = StopBits.One;    //  1 ストップビット

                try
                {
                    serialPort.Open();             // シリアルポートをオープンする
                    serialPort.DiscardInBuffer();  // 受信バッファのクリア


                    ComPortComboBox.IsEnabled = false;        // 通信条件等を選択不可にする。

                 //   ComPortSearchButton.IsEnabled = false;    // 通信ポート検索ボタンを無効とする。

                    OpenInfoTextBox.Text = " Open (" + serialPort.PortName + ")";

                    ComPortOpenButton.Content = "Close";      // ボタン表示を OpenからCloseへ
                }
                catch
                {
                    OpenInfoTextBox.Text = "(" + serialPort.PortName + ") is opend by another.";
                }
            }
        }



    }
}
