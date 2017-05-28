using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;

// Test program for Pulse Train Hat http://www.pthat.com

namespace PulseTrainHatBufferExample
{
    public sealed partial class MainPage : Page
    {
        /// <summary>
        /// Private variables
        /// </summary>
        private SerialDevice serialPort = null;

        private DataWriter dataWriteObject = null;
        private DataReader dataReaderObject = null;

        private ObservableCollection<DeviceInformation> listOfDevices;
        private CancellationTokenSource ReadCancellationTokenSource;

        private int selectlinestart = 0;//Used for highlighting current Gcode line

        public static class MyStaticValues
        {
            public static List<double> XMainCommandCount = new List<double>(); // Used for updating X Position box after receiving command back
            public static List<double> YMainCommandCount = new List<double>(); // Used for updating Y Position box after receiving command back
            public static List<double> ZMainCommandCount = new List<double>(); // Used for updating Z Position box after receiving command back
            public static List<double> EMainCommandCount = new List<double>(); // Used for updating E Position box after receiving command back

            public static int BackinXcommandcount = 0;  // Used for updating Position boxes after receiving command back
            public static int BackinYcommandcount = 0;  // Used for updating Position boxes after receiving command back
            public static int BackinZcommandcount = 0;  // Used for updating Position boxes after receiving command back
            public static int BackinEcommandcount = 0;  // Used for updating Position boxes after receiving command back

            public static double XcompletedPosition = 0; //For storing X Position
            public static double YcompletedPosition = 0; //For storing Y Position
            public static double ZcompletedPosition = 0; //For storing Z Position
            public static double EcompletedPosition = 0; //For storing E Position

            public static int NextCommand = 0; //Used when clear to send next command

            public static int startbuffersend = 0; //Used to specify if buffer command store
            public static int Running = 0; //Running or not

            public static int Xset = 0; //X-Axis set
            public static int Yset = 0; //Y-Axis set
            public static int Zset = 0; //Z-Axis set
            public static int Eset = 0; //E-Axis set

            //------------Jog variables

            //----Jog status:
            // 0: pressed
            // 1: enabled
            // 2: disabled
            public static int Jenable = 2;

            //Switch case to determine which axis and direction is active
            public static string JogAxis = "";

            //Switch case for whether a button is pressed or released
            public static string JogAction = "";

            //Stores the Set Axis Command
          //  public static string sendstore = "";

            //Catches a button release event
            public static int XCatch = 0;
            public static int YCatch = 0;
            public static int ZCatch = 0;

            //Stores Axis Position
            public static string STORETMP = "";

            //Set Aux flag
            public static int AuxDetect = 2;

            //Go to zero flag
            public static int GotoZ = 0;
        }

        public MainPage()
        {
            this.InitializeComponent();

            DisableJog();
            comPortInput.IsEnabled = false;
            Firmware1.IsEnabled = false;
            StartAll.IsEnabled = false;

            StopAll.IsEnabled = false;
            Reset.IsEnabled = false;
            ToggleEnableLine.IsEnabled = false;
            calculatetravelspeeds();

            listOfDevices = new ObservableCollection<DeviceInformation>();
            ListAvailablePorts();
            //  MyStaticValues.stopwatch.Start();
        }

        /// <summary>
        /// ListAvailablePorts
        /// - Use SerialDevice.GetDeviceSelector to enumerate all serial devices
        /// - Attaches the DeviceInformation to the ListBox source so that DeviceIds are displayed
        /// </summary>
        private async void ListAvailablePorts()
        {
            try
            {
                string aqs = SerialDevice.GetDeviceSelector();
                var dis = await DeviceInformation.FindAllAsync(aqs);

                status.Text = "Select a device and connect";

                for (int i = 0; i < dis.Count; i++)
                {
                    listOfDevices.Add(dis[i]);
                }

                DeviceListSource.Source = listOfDevices;
                comPortInput.IsEnabled = true;
                ConnectDevices.SelectedIndex = -1;
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
            }
        }

        /// <summary>
        /// comPortInput_Click: Action to take when 'Connect' button is clicked
        /// - Get the selected device index and use Id to create the SerialDevice object
        /// - Configure default settings for the serial port
        /// - Create the ReadCancellationTokenSource token
        /// - Start listening on the serial port input
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void comPortInput_Click(object sender, RoutedEventArgs e)
        {
            var selection = ConnectDevices.SelectedItems;

            if (selection.Count <= 0)
            {
                status.Text = "Select a device and connect";
                return;
            }

            DeviceInformation entry = (DeviceInformation)selection[0];

            try
            {
                serialPort = await SerialDevice.FromIdAsync(entry.Id);

                // Disable the 'Connect' button
                comPortInput.IsEnabled = false;

                // Configure serial settings
                serialPort.WriteTimeout = TimeSpan.FromMilliseconds(30);
                serialPort.ReadTimeout = TimeSpan.FromMilliseconds(30);

                if (LowSpeedBaud.IsChecked == true)
                {
                    serialPort.BaudRate = 115200;
                }
                else
                {
                    serialPort.BaudRate = 806400;
                }

                serialPort.Parity = SerialParity.None;
                serialPort.StopBits = SerialStopBitCount.One;
                serialPort.DataBits = 8;
                serialPort.Handshake = SerialHandshake.None;

                // Display configured settings
                status.Text = "Serial port configured successfully: ";
                status.Text += serialPort.BaudRate + "-";
                status.Text += serialPort.DataBits + "-";
                status.Text += serialPort.Parity.ToString() + "-";
                status.Text += serialPort.StopBits;

                // Set the RcvdText field to invoke the TextChanged callback
                // The callback launches an async Read task to wait for data
                rcvdText.Text = "Waiting for data...";

                // Create cancellation token object to close I/O operations when closing the device
                ReadCancellationTokenSource = new CancellationTokenSource();

                // Enable 'Start' button to allow sending data
                EnableJog();
                Firmware1.IsEnabled = true;
                Reset.IsEnabled = true;
                ToggleEnableLine.IsEnabled = true;
                sendText.Text = "";
                StartAll.IsEnabled = true;

                Listen();
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
                comPortInput.IsEnabled = true;
                DisableJog();
                Firmware1.IsEnabled = false;
                StartAll.IsEnabled = false;
                //  PauseAll.IsEnabled = false;
                StopAll.IsEnabled = false;
                Reset.IsEnabled = false;
                ToggleEnableLine.IsEnabled = false;
            }
        }

        /// <summary>
        /// WriteAsync: Task that asynchronously writes data from the input text box 'sendText' to the OutputStream
        /// </summary>
        /// <returns></returns>
        private async Task WriteAsync()
        {
            Task<UInt32> storeAsyncTask;

            // Load the text from the sendText input text box to the dataWriter object
            dataWriteObject.WriteString(sendText.Text);

            // Launch an async task to complete the write operation
            storeAsyncTask = dataWriteObject.StoreAsync().AsTask();

            UInt32 bytesWritten = await storeAsyncTask;
            if (bytesWritten > 0)
            {
                status.Text = sendText.Text + ", ";
                status.Text += "bytes written successfully!";
            }
        }

        /// <summary>
        /// - Create a DataReader object
        /// - Create an async task to read from the SerialDevice InputStream
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void Listen()
        {
            try
            {
                if (serialPort != null)
                {
                    dataReaderObject = new DataReader(serialPort.InputStream);

                    // keep reading the serial input
                    while (true)
                    {
                        await ReadAsync(ReadCancellationTokenSource.Token);
                        //     await ReadAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex.GetType().Name == "TaskCanceledException")
                {
                    status.Text = "Reading task was cancelled, closing device and cleaning up";
                    CloseDevice();
                }
                else
                {
                    status.Text = ex.Message;
                }
            }
            finally
            {
                // Cleanup once complete
                if (dataReaderObject != null)
                {
                    dataReaderObject.DetachStream();
                    dataReaderObject = null;
                }
            }
        }

        /// <summary>
        /// ReadAsync: Task that waits on data and reads asynchronously from the serial device InputStream
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task ReadAsync(CancellationToken cancellationToken)
        //private async Task ReadAsync()
        {
            Task<UInt32> loadAsyncTask;

            uint ReadBufferLength = 1024;
            double Calc;
            // If task cancellation was requested, comply
            cancellationToken.ThrowIfCancellationRequested();

            // Set InputStreamOptions to complete the asynchronous read operation when one or more bytes is available
            dataReaderObject.InputStreamOptions = InputStreamOptions.Partial;

            // Create a task object to wait for data on the serialPort.InputStream
            loadAsyncTask = dataReaderObject.LoadAsync(ReadBufferLength).AsTask(cancellationToken);

            //    loadAsyncTask = dataReaderObject.LoadAsync(ReadBufferLength).AsTask();

            // Launch the task and wait
            UInt32 bytesRead = await loadAsyncTask;

            if (bytesRead > 0)
            {
                rcvdText.Text = dataReaderObject.ReadString(bytesRead);
                string input = rcvdText.Text;

                Debug.WriteLine(rcvdText.Text);

                //Check if received message can be divided by 7 as our return messages are 7 bytes long
                if (input.Length % 7 == 0)

                {
                    for (int i = 0; i < input.Length; i += 7)
                    //  foreach (string match in sub)

                    {
                        string sub = input.Substring(i, 7);

                        //********************************************************************-Instant Commands

                        //Check if Start ALL command Received
                        if (sub == "RI00SA*")
                        {
                            //Enable/Disable certain controls
                            StartAll.IsEnabled = false;
                            StopAll.IsEnabled = true;
                            ToggleEnableLine.IsEnabled = false;
                            Firmware1.IsEnabled = false;
                        }

                        //X Start Recieved
                        if (sub == "RI00SX*")
                        {
                            //X has a caught a button release event
                            if (MyStaticValues.XCatch == 1)
                            {
                                //Disables jog
                                MyStaticValues.Jenable = 2;

                                //Sets action to released
                                MyStaticValues.JogAction = "released";

                                //Initialises jog set method
                                InitJog();
                            }
                            //sets catch to active
                            MyStaticValues.XCatch = 1;
                        }

                        //Y Start Recieved
                        if (sub == "RI00SY*")
                        {
                            if (MyStaticValues.YCatch == 1)
                            {
                                MyStaticValues.Jenable = 2;
                                MyStaticValues.JogAction = "released";
                                InitJog();
                            }
                            MyStaticValues.YCatch = 1;
                        }

                        //Z Start Recieved
                        if (sub == "RI00SZ*")
                        {
                            if (MyStaticValues.ZCatch == 1)
                            {
                                MyStaticValues.Jenable = 2;
                                MyStaticValues.JogAction = "released";
                                InitJog();
                            }
                            MyStaticValues.ZCatch = 1;
                        }

                        //Check if Set X Axis completed
                        if (sub == "CI00CX*")
                        {
                            // Sends out a start X Axis
                            sendText.Text = "I00SX*";
                            SendDataout();
                        }

                        //Check if Set Y Axis completed
                        if (sub == "CI00CY*")
                        {
                            //sends out a Start Y Axis
                            sendText.Text = "I00SY*";
                            SendDataout();
                        }

                        //Check if Set Z Axis completed
                        if (sub == "CI00CZ*")
                        {
                            //sends out a Start Z Axis
                            sendText.Text = "I00SZ*";
                            SendDataout();
                        }

                        //Check if X Axis completed amount of pulses
                        if (sub == "CI00SX*")
                        {
                            //sends out a Get X Pulses
                            sendText.Text = "I00XP*";
                            SendDataout();
                        }

                        //Check if Y Axis completed amount of pulses
                        if (sub == "CI00SY*")
                        {
                            //sends out a Get Y Pulses
                            sendText.Text = "I00YP*";
                            SendDataout();
                        }

                        //Check if Z Axis completed amount of pulses
                        if (sub == "CI00SZ*")
                        {
                            //sends out a Get Z Pulses
                            sendText.Text = "I00ZP*";
                            SendDataout();
                        }

                        //X Request Current Pulse Count Reply Recieved!
                        if (sub == "RI00XP*")
                        {
                            //Multiplies the X pulses by the X resolution to work out our millimeters
                            Calc = Convert.ToDouble(rcvdText.Text.Substring(i + 10, 10)) * Convert.ToDouble(XResolution.Text);

                            //Updates X Position based on pin direction
                            XPosition.Text = (rcvdText.Text.Substring(i + 9, 1) == PinX.Text) ? String.Format("{0:0000.00000}", Convert.ToDouble(MyStaticValues.STORETMP) + Calc) : String.Format("{0:0000.00000}", Convert.ToDouble(MyStaticValues.STORETMP) - Calc);
                        }

                        //X Request Current Pulse Count Reply Complete
                        if (sub == "CI00XP*")
                        {
                            //Enables jog
                            MyStaticValues.Jenable = 1;

                            //resets trigger
                            MyStaticValues.XCatch = 0;
                        }

                        //Y Request Current Pulse Count Reply Recieved!
                        if (sub == "RI00YP*")
                        {
                            Calc = Convert.ToDouble(rcvdText.Text.Substring(i + 10, 10)) * Convert.ToDouble(YResolution.Text);
                            YPosition.Text = (rcvdText.Text.Substring(i + 9, 1) == PinY.Text) ? String.Format("{0:0000.00000}", Convert.ToDouble(MyStaticValues.STORETMP) + Calc) : String.Format("{0:0000.00000}", Convert.ToDouble(MyStaticValues.STORETMP) - Calc);
                        }

                        //Y Request Current Pulse Count Reply Complete
                        if (sub == "CI00YP*")
                        {
                            MyStaticValues.Jenable = 1;
                            MyStaticValues.YCatch = 0;
                        }

                        //Z Request Current Pulse Count Reply Recieved!
                        if (sub == "RI00ZP*")
                        {
                            Calc = Convert.ToDouble(rcvdText.Text.Substring(i + 10, 10)) * Convert.ToDouble(ZResolution.Text);
                            ZPosition.Text = (rcvdText.Text.Substring(i + 9, 1) == PinZ.Text) ? String.Format("{0:0000.00000}", Convert.ToDouble(MyStaticValues.STORETMP) + Calc) : String.Format("{0:0000.00000}", Convert.ToDouble(MyStaticValues.STORETMP) - Calc);
                        }

                        //Z Request Current Pulse Count Reply Complete
                        if (sub == "CI00ZP*")
                        {
                            MyStaticValues.Jenable = 1;
                            MyStaticValues.ZCatch = 0;
                        }

                        //Check For Firmware reply Back
                        if (sub == "RI00FW*")
                        {
                            rcvdText.Text = rcvdText.Text.Substring(i + 8, 40);
                        }

                        //Check if ALL Axis Stop button Complete
                        if (sub == "CI00TA*")
                        {
                            StopAll.IsEnabled = false;
                            StartAll.IsEnabled = true;
                            ToggleEnableLine.IsEnabled = true;
                            Firmware1.IsEnabled = true;
                        }

                        //********************************************************************-Buffer Commands

                        //Monitor buffer count for initial start
                        if (MyStaticValues.startbuffersend == 1)
                        {
                            //Check if Start All received in buffer
                            if (sub == "RB00SA*")
                            {
                                MyStaticValues.NextCommand = 1;
                            }

                            //Check if Set X Axis received in buffer
                            if (sub == "RB00CX*")
                            {
                                MyStaticValues.NextCommand = 1;
                            }

                            //Check if Set Y Axis received in buffer
                            if (sub == "RB00CY*")
                            {
                                MyStaticValues.NextCommand = 1;
                            }

                            //Check if Set Z Axis received in buffer
                            if (sub == "RB00CZ*")
                            {
                                MyStaticValues.NextCommand = 1;
                            }

                            //Check if Set E Axis received in buffer
                            if (sub == "RB00CE*")
                            {
                                MyStaticValues.NextCommand = 1;
                            }

                            //Check if Initialise Buffer received
                            if (input == "RBH000*")
                            {
                                MyStaticValues.NextCommand = 1;
                            }

                            //Check if wait command received
                            if (sub == "RB00WW*")
                            {
                                MyStaticValues.NextCommand = 1;
                            }

                            //Check if Aux received in buffer
                            if (sub == "RB00A1*")
                            {
                                MyStaticValues.NextCommand = 1;
                            }

                            //Check if Aux received in buffer
                            if (sub == "RB00A2*")
                            {
                                MyStaticValues.NextCommand = 1;
                            }

                            //Check if Aux received in buffer
                            if (sub == "RB00A3*")
                            {
                                MyStaticValues.NextCommand = 1;
                            }
                        }
                        else
                        {
                            //Check if Aux received in buffer
                            if (sub == "RB00A1*")
                            {
                                MyStaticValues.NextCommand = 1;
                            }

                            //Check if wait command finished
                            if (sub == "CB00WW*")
                            {
                                MyStaticValues.NextCommand = 1;
                            }

                            //Check if Aux received in buffer
                            if (sub == "RB00A2*")
                            {
                                MyStaticValues.NextCommand = 1;
                            }

                            //Check if Aux received in buffer
                            if (sub == "RB00A3*")
                            {
                                MyStaticValues.NextCommand = 1;
                            }

                            //Check if Start All received in buffer
                            if (sub == "CB00SA*")
                            {
                                MyStaticValues.NextCommand = 1;
                                // Nextcountout = 4;
                            }

                            //Check if Set X Axis received in buffer
                            if (sub == "RB00CX*")
                            {
                                MyStaticValues.NextCommand = 1;
                            }

                            //Check if Set Y Axis received in buffer
                            if (sub == "RB00CY*")
                            {
                                MyStaticValues.NextCommand = 1;
                            }

                            //Check if Set Z Axis received in buffer
                            if (sub == "RB00CZ*")
                            {
                                MyStaticValues.NextCommand = 1;
                            }

                            //Check if Set E Axis received in buffer
                            if (sub == "RB00CE*")
                            {
                                MyStaticValues.NextCommand = 1;
                            }

                            //Check if X Axis completed amount of pulses
                            if (sub == "CB00SX*")
                            {
                                MyStaticValues.Xset = MyStaticValues.Xset - 1;

                                if (MyStaticValues.GotoZ == 0)
                                {
                                    XPosition.Text = Convert.ToString(MyStaticValues.XMainCommandCount[MyStaticValues.BackinXcommandcount]);
                                    XPosition.Text = String.Format("{0:0000.00000}", Convert.ToDouble(XPosition.Text));
                                    MyStaticValues.BackinXcommandcount = MyStaticValues.BackinXcommandcount + 1;
                                }
                                else
                                {
                                    XPosition.Text = "0000.00000";
                                }

                                //Check if Y Axis completed amount of pulses
                                if (sub == "CB00SY*")
                                {
                                    MyStaticValues.Yset = MyStaticValues.Yset - 1;

                                    if (MyStaticValues.GotoZ == 0)
                                    {
                                        YPosition.Text = Convert.ToString(MyStaticValues.YMainCommandCount[MyStaticValues.BackinYcommandcount]);
                                        YPosition.Text = String.Format("{0:0000.00000}", Convert.ToDouble(YPosition.Text));
                                        MyStaticValues.BackinYcommandcount = MyStaticValues.BackinYcommandcount + 1;
                                    }
                                    else
                                    {
                                        YPosition.Text = "0000.00000";
                                    }
                                }

                                //Check if Z Axis completed amount of pulses
                                if (sub == "CB00SZ*")
                                {
                                    MyStaticValues.Zset = MyStaticValues.Zset - 1;

                                    if (MyStaticValues.GotoZ == 0)
                                    {
                                        ZPosition.Text = Convert.ToString(MyStaticValues.ZMainCommandCount[MyStaticValues.BackinZcommandcount]);
                                        ZPosition.Text = String.Format("{0:0000.00000}", Convert.ToDouble(ZPosition.Text));
                                        MyStaticValues.BackinZcommandcount = MyStaticValues.BackinZcommandcount + 1;
                                    }
                                    else
                                    {
                                        ZPosition.Text = "0000.00000";
                                    }
                                }

                                //Check if E Axis completed amount of pulses
                                if (sub == "CB00SE*")
                                {
                                    MyStaticValues.Eset = MyStaticValues.Eset - 1;

                                    if (MyStaticValues.GotoZ == 0)
                                    {
                                        EPosition.Text = Convert.ToString(MyStaticValues.EMainCommandCount[MyStaticValues.BackinEcommandcount]);
                                        EPosition.Text = String.Format("{0:0000.00000}", Convert.ToDouble(EPosition.Text));
                                        MyStaticValues.BackinEcommandcount = MyStaticValues.BackinEcommandcount + 1;
                                    }
                                    else
                                    {
                                        EPosition.Text = "0000.00000";
                                    }
                                }

                                int checkall = MyStaticValues.Xset + MyStaticValues.Yset + MyStaticValues.Zset + MyStaticValues.Eset;
                                if (checkall == 0)
                                {
                                    MyStaticValues.GotoZ = 0;
                                    StopAll.IsEnabled = false;
                                    StartAll.IsEnabled = true;
                                    ToggleEnableLine.IsEnabled = true;
                                    Firmware1.IsEnabled = true;
                                    AddCommand1.IsEnabled = true;
                                    AddAux1.IsEnabled = true;
                                    AddAux2.IsEnabled = true;
                                    AddAux3.IsEnabled = true;
                                    ClearWindow.IsEnabled = true;
                                    Store.IsEnabled = true;
                                }
                            }
                        } //End of for loop
                    } //End of checking length if
                } //End of checking for bytes
            } //End of byte read
        } //End of async read

        /// <summary>
        /// CancelReadTask:
        /// - Uses the ReadCancellationTokenSource to cancel read operations
        /// </summary>
        private void CancelReadTask()
        {
            if (ReadCancellationTokenSource != null)
            {
                if (!ReadCancellationTokenSource.IsCancellationRequested)
                {
                    ReadCancellationTokenSource.Cancel();
                }
            }
        }

        /// <summary>
        /// CloseDevice:
        /// - Disposes SerialDevice object
        /// - Clears the enumerated device Id list
        /// </summary>
        private void CloseDevice()
        {
            if (serialPort != null)
            {
                serialPort.Dispose();
            }
            serialPort = null;
            comPortInput.IsEnabled = true;
            rcvdText.Text = "";
            listOfDevices.Clear();
        }

        /// <summary>
        /// closeDevice_Click: Action to take when 'Disconnect and Refresh List' is clicked on
        /// - Cancel all read operations
        /// - Close and dispose the SerialDevice object
        /// - Enumerate connected devices
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void closeDevice_Click(object sender, RoutedEventArgs e)
        {
            Disconnectserial();
        }

        private void Disconnectserial()
        {
            try
            {
                status.Text = "";
                CancelReadTask();
                CloseDevice();
                ListAvailablePorts();
                Firmware1.IsEnabled = false;
                StartAll.IsEnabled = false;
                StopAll.IsEnabled = false;
                Reset.IsEnabled = false;
                DisableJog();
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
            }
        }

        private void Firmware_Click(object sender, RoutedEventArgs e)
        {
            sendText.Text = "I00FW*";
            SendDataout();
        }

        private async void SendDataout()
        {
            try
            {
                if (serialPort != null)
                {
                    // Create the DataWriter object and attach to OutputStream
                    dataWriteObject = new DataWriter(serialPort.OutputStream);

                    //Launch the WriteAsync task to perform the write
                    await WriteAsync();
                }
                else
                {
                    status.Text = "Select a device and connect";
                }
            }
            catch (Exception ex)
            {
                status.Text = "Send Data: " + ex.Message;
            }
            finally
            {
                // Cleanup once complete
                if (dataWriteObject != null)
                {
                    dataWriteObject.DetachStream();
                    dataWriteObject = null;
                }
            }
        }

        private void StartAll_Click(object sender, RoutedEventArgs e)
        {
            MyStaticValues.GotoZ = 0;
            MyStaticValues.Xset = 0;
            MyStaticValues.Yset = 0;
            MyStaticValues.Zset = 0;
            MyStaticValues.Eset = 0;
            StopAll.IsEnabled = true;

            StartAll.IsEnabled = false;
            ToggleEnableLine.IsEnabled = false;
            Firmware1.IsEnabled = false;
            AddCommand1.IsEnabled = false;
            AddAux1.IsEnabled = false;
            AddAux2.IsEnabled = false;
            AddAux3.IsEnabled = false;

            if (Zeroonstart.IsChecked == true)
            {
                XPosition.Text = "0000.00000";
                MyStaticValues.XcompletedPosition = 0;
                YPosition.Text = "0000.00000";
                MyStaticValues.YcompletedPosition = 0;
                ZPosition.Text = "0000.00000";
                MyStaticValues.ZcompletedPosition = 0;
                EPosition.Text = "0000.00000";
                MyStaticValues.EcompletedPosition = 0;
            }

            MyStaticValues.BackinXcommandcount = 0;
            MyStaticValues.BackinYcommandcount = 0;
            MyStaticValues.BackinZcommandcount = 0;
            MyStaticValues.BackinEcommandcount = 0;

            MyStaticValues.Running = 1;
            SendoutFormattedCommands();
        }

        private void StopAll_Click(object sender, RoutedEventArgs e)
        {
            sendText.Text = "I00TA*";
            SendDataout();
            MyStaticValues.Running = 0;
            StopAll.IsEnabled = false;
            StartAll.IsEnabled = true;
            ToggleEnableLine.IsEnabled = true;
            Firmware1.IsEnabled = true;
            AddCommand1.IsEnabled = true;
            AddAux1.IsEnabled = true;
            AddAux2.IsEnabled = true;
            AddAux3.IsEnabled = true;
            ClearWindow.IsEnabled = true;
            Store.IsEnabled = true;
            MyStaticValues.GotoZ = 0;
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            sendText.Text = "N*";
            SendDataout();
            Disconnectserial();
        }

        private void XmmMIN_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!String.IsNullOrEmpty(XmmMIN.Text.Trim()))
            {
                calculatetravelspeeds();
            }
        }

        private void YmmMIN_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!String.IsNullOrEmpty(YmmMIN.Text.Trim()))
            {
                calculatetravelspeeds();
            }
        }

        private void ZmmMIN_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!String.IsNullOrEmpty(ZmmMIN.Text.Trim()))
            {
                calculatetravelspeeds();
            }
        }

        private void EmmMIN_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!String.IsNullOrEmpty(EmmMIN.Text.Trim()))
            {
                calculatetravelspeeds();
            }
        }

        private void XStepsPerMM_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!String.IsNullOrEmpty(XStepsPerMM.Text.Trim()))
            {
                calculatetravelspeeds();
            }
        }

        private void YStepsPerMM_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!String.IsNullOrEmpty(YStepsPerMM.Text.Trim()))
            {
                calculatetravelspeeds();
            }
        }

        private void ZStepsPerMM_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!String.IsNullOrEmpty(ZStepsPerMM.Text.Trim()))
            {
                calculatetravelspeeds();
            }
        }

        private void EStepsPerMM_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!String.IsNullOrEmpty(EStepsPerMM.Text.Trim()))
            {
                calculatetravelspeeds();
            }
        }

        private void calculatetravelspeeds()
        {
            XHZresult.Text = String.Format("{0:000000.000}", (Convert.ToDouble(XmmMIN.Text) / 60) * Convert.ToDouble(XStepsPerMM.Text));
            XResolution.Text = Convert.ToString(1.0 / Convert.ToDouble(XStepsPerMM.Text));

            YHZresult.Text = String.Format("{0:000000.000}", (Convert.ToDouble(YmmMIN.Text) / 60) * Convert.ToDouble(YStepsPerMM.Text));
            YResolution.Text = Convert.ToString(1.0 / Convert.ToDouble(YStepsPerMM.Text));

            ZHZresult.Text = String.Format("{0:000000.000}", (Convert.ToDouble(ZmmMIN.Text) / 60) * Convert.ToDouble(ZStepsPerMM.Text));
            ZResolution.Text = Convert.ToString(1.0 / Convert.ToDouble(ZStepsPerMM.Text));

            EHZresult.Text = String.Format("{0:000000.000}", (Convert.ToDouble(EmmMIN.Text) / 60) * Convert.ToDouble(EStepsPerMM.Text));
            EResolution.Text = Convert.ToString(1.0 / Convert.ToDouble(EStepsPerMM.Text));
        }

        private void ToggleEnableLine_Click(object sender, RoutedEventArgs e)
        {
            sendText.Text = "I00HT*";
            SendDataout();
        }

        private void ResetX_Click(object sender, RoutedEventArgs e)
        {
            XPosition.Text = "0000.00000";
        }

        private void ResetY_Click(object sender, RoutedEventArgs e)
        {
            YPosition.Text = "0000.00000";
        }

        private void ResetZ_Click(object sender, RoutedEventArgs e)
        {
            ZPosition.Text = "0000.00000";
        }

        private void ResetE_Click(object sender, RoutedEventArgs e)
        {
            EPosition.Text = "0000.00000";
        }

        private async void SendoutFormattedCommands()
        {
            int Nextcountout = 0;

            //Get number of commands to send out to buffer
            MyStaticValues.NextCommand = 0;
            var PTHATLines = new string[] { };
            PTHATLines = pthatoutput.Text.Split('\n');

            MyStaticValues.startbuffersend = 1;
            selectlinestart = 0;

            //Initialise buffer
            sendText.Text = "H0000*";
            SendDataout();

            waitforinit:
            if (MyStaticValues.Running == 0)
            {
                goto Stoptriggered;
            }

            await Task.Delay(1);

            if (MyStaticValues.NextCommand == 0)
            {
                goto waitforinit;
            }

            if (PTHATLines.Length - 1 <= 21)
            {
                Nextcountout = PTHATLines.Length - 2;
            }
            else
            {
                Nextcountout = 20;
            }

            for (int Plines = 0; Plines < PTHATLines.Length - 1; Plines++)
            {
                pthatoutput.Focus(FocusState.Keyboard);
                pthatoutput.Select(selectlinestart, PTHATLines[Plines].Length);
                selectlinestart = selectlinestart + PTHATLines[Plines].Length;

                //Check if a comment
                if (PTHATLines[Plines].Substring(0, 1) != ";")
                {
                    //Check if a Set Axis Command
                    if (PTHATLines[Plines].Substring(0, 5) == "B00CX")
                    {
                        sendText.Text = PTHATLines[Plines];
                        MyStaticValues.NextCommand = 0;
                        MyStaticValues.Xset = MyStaticValues.Xset + 1;
                        SendDataout();
                    }

                    //Check if a Set Axis Command
                    if (PTHATLines[Plines].Substring(0, 5) == "B00CY")
                    {
                        sendText.Text = PTHATLines[Plines];
                        MyStaticValues.NextCommand = 0;
                        MyStaticValues.Yset = MyStaticValues.Yset + 1;
                        SendDataout();
                    }

                    if (PTHATLines[Plines].Substring(0, 5) == "B00CZ")
                    {
                        sendText.Text = PTHATLines[Plines];
                        MyStaticValues.NextCommand = 0;
                        MyStaticValues.Zset = MyStaticValues.Zset + 1;
                        SendDataout();
                    }

                    if (PTHATLines[Plines].Substring(0, 5) == "B00CE")
                    {
                        sendText.Text = PTHATLines[Plines];
                        MyStaticValues.NextCommand = 0;
                        MyStaticValues.Eset = MyStaticValues.Eset + 1;
                        SendDataout();
                    }

                    //Check if a Set Aux outputCommand
                    if (PTHATLines[Plines].Substring(0, 4) == "B00A")
                    {
                        sendText.Text = PTHATLines[Plines];
                        MyStaticValues.NextCommand = 0;

                        SendDataout();
                    }

                    //Check if a Send Start All Command
                    if (PTHATLines[Plines].Substring(0, 5) == "B00SA")
                    {
                        sendText.Text = PTHATLines[Plines];
                        MyStaticValues.NextCommand = 0;
                        SendDataout();
                    }

                    //Check if a Send Delay
                    if (PTHATLines[Plines].Substring(0, 5) == "B00WW")
                    {
                        sendText.Text = PTHATLines[Plines];
                        MyStaticValues.NextCommand = 0;
                        SendDataout();
                    }
                }

                if (Plines == Nextcountout)
                {
                    MyStaticValues.startbuffersend = 0;
                    MyStaticValues.NextCommand = 0;
                    await Task.Delay(100);
                    sendText.Text = "Z0000*";
                    SendDataout();
                }

                waitforok:

                if (MyStaticValues.Running == 0)
                {
                    goto Stoptriggered;
                }
                await Task.Delay(10);

                if (MyStaticValues.NextCommand == 0)
                {
                    goto waitforok;
                }
            } // End of Plines loop

            Stoptriggered:;
        } //end of Sendoutformatted commands

        private void AddAux1_Click(object sender, RoutedEventArgs e)
        {
            if (Aux1_Off.IsChecked == true)
            {
                //Sends Aux1 Off Command
                pthatoutput.Text = pthatoutput.Text + "B00A10*" + "\n";
            }

            if (Aux1_On.IsChecked == true)
            {
                //Sends Aux1 On Command
                pthatoutput.Text = pthatoutput.Text + "B00A11*" + "\n";
            }
        }

        private void AddAux2_Click(object sender, RoutedEventArgs e)
        {
            if (Aux2_Off.IsChecked == true)
            {
                pthatoutput.Text = pthatoutput.Text + "B00A20*" + "\n";
            }

            if (Aux2_On.IsChecked == true)
            {
                pthatoutput.Text = pthatoutput.Text + "B00A21*" + "\n";
            }
        }

        private void AddAux3_Click(object sender, RoutedEventArgs e)
        {
            if (Aux3_Off.IsChecked == true)
            {
                pthatoutput.Text = pthatoutput.Text + "B00A30*" + "\n";
            }

            if (Aux3_On.IsChecked == true)
            {
                pthatoutput.Text = pthatoutput.Text + "B00A31*" + "\n";
            }
        }

        private async void AddCommandtoscript()
        {
            int Xdir = Convert.ToInt16(PinX.Text);
            int Ydir = Convert.ToInt16(PinY.Text);
            int Zdir = Convert.ToInt16(PinZ.Text);
            int Edir = Convert.ToInt16(PinE.Text);
            string direc;
            int pulses = 0;
            double temp = 0;

            if (IncludeX.IsChecked == true)
            {
                //Check if Incremental or Absolute Movement
                if (Abso_Off.IsChecked == true)
                {
                    //Make sure desired travel distance can be divided by resolution, if not correct Distance
                    temp = Convert.ToDouble(DistanceX.Text) / Convert.ToDouble(XResolution.Text);
                    pulses = Math.Abs(Convert.ToInt32(temp));
                    DistanceX.Text = Convert.ToString(pulses * Convert.ToDouble(XResolution.Text));

                    //Check X Direction Checkbox
                    if (Xleft.IsChecked == true)
                    {
                        //Set direction
                        direc = "Left";

                        //Change Xdir based on direction pin
                        Xdir = (PinX.Text == "1") ? 0 : 1;
                    }
                    else
                    {
                        direc = "Right";
                        Xdir = (PinX.Text == "1") ? 1 : 0;
                    }

                    if (EnableComments.IsChecked == true)
                    {
                        //Outputs a comment
                        pthatoutput.Text = pthatoutput.Text + "; Set Move X Axis " + DistanceX.Text + "mm" + " to the " + direc + "\n";
                    }

                    //Stores completed position
                    MyStaticValues.XcompletedPosition = (direc == "Right") ? MyStaticValues.XcompletedPosition + Convert.ToDouble(DistanceX.Text) : MyStaticValues.XcompletedPosition - Convert.ToDouble(DistanceX.Text);

                    if (EnableComments.IsChecked == true)
                    {
                        pthatoutput.Text = pthatoutput.Text + "; X position after move " + String.Format("{0:0000.00000}", MyStaticValues.XcompletedPosition) + "mm" + "\n";
                    }

                    //Adds Buffer Command to Textbox
                    pthatoutput.Text = pthatoutput.Text + "B00CX" + String.Format("{0:000000.000}", XHZresult.Text) + String.Format("{0:0000000000}", pulses) + String.Format("{0:0}", Xdir) + "1" + "1" + String.Format("{0:000}", Convert.ToInt16(rampdivide.Text)) + String.Format("{0:000}", Convert.ToInt16(ramppause.Text)) + "0" + String.Format("{0:0}", Convert.ToInt16(EnablePolarity.Text)) + "*" + "\n";

                    if (EnableComments.IsChecked == true)
                    {
                        pthatoutput.Text = pthatoutput.Text + ";" + "\n";
                    }

                    //Increment Count
                    MyStaticValues.XMainCommandCount.Add(MyStaticValues.XcompletedPosition);
                }
                else
                {
                    //Check if target position is greater than current position
                    if (Convert.ToDouble(DistanceX.Text) >= MyStaticValues.XcompletedPosition)
                    {
                        //If distance is not equal to completed position
                        if (Convert.ToDouble(DistanceX.Text) != MyStaticValues.XcompletedPosition)
                        {
                            direc = "Right";
                            Xdir = (PinX.Text == "1") ? 1 : 0;

                            if (EnableComments.IsChecked == true)
                            {
                                pthatoutput.Text = pthatoutput.Text + "; Set Move X Axis " + String.Format("{0:0000.00000}", Convert.ToDouble(DistanceX.Text) - MyStaticValues.XcompletedPosition) + "mm" + " to the " + direc + "\n";
                            }

                            //Calculate pulses
                            temp = (Convert.ToDouble(DistanceX.Text) - MyStaticValues.XcompletedPosition) / Convert.ToDouble(XResolution.Text);

                            //Convert to int value
                            pulses = Math.Abs(Convert.ToInt32(temp));

                            MyStaticValues.XcompletedPosition = Convert.ToDouble(DistanceX.Text);

                            if (EnableComments.IsChecked == true)
                            {
                                pthatoutput.Text = pthatoutput.Text + "; X position after move " + String.Format("{0:0000.00000}", MyStaticValues.XcompletedPosition) + "mm" + "\n";
                            }

                            pthatoutput.Text = pthatoutput.Text + "B00CX" + String.Format("{0:000000.000}", XHZresult.Text) + String.Format("{0:0000000000}", pulses) + String.Format("{0:0}", Xdir) + "1" + "1" + String.Format("{0:000}", Convert.ToInt16(rampdivide.Text)) + String.Format("{0:000}", Convert.ToInt16(ramppause.Text)) + "0" + String.Format("{0:0}", Convert.ToInt16(EnablePolarity.Text)) + "*" + "\n";

                            if (EnableComments.IsChecked == true)
                            {
                                pthatoutput.Text = pthatoutput.Text + ";" + "\n";
                            }
                            MyStaticValues.XMainCommandCount.Add(MyStaticValues.XcompletedPosition);
                        }
                    }
                    else //Is less
                    {
                        direc = "Left";
                        Xdir = (PinX.Text == "1") ? 0 : 1;

                        if (EnableComments.IsChecked == true)
                        {
                            pthatoutput.Text = pthatoutput.Text + "; Set Move X Axis " + String.Format("{0:0000.00000}", MyStaticValues.XcompletedPosition - Convert.ToDouble(DistanceX.Text)) + "mm" + " to the " + direc + "\n";
                        }

                        temp = (MyStaticValues.XcompletedPosition - Convert.ToDouble(DistanceX.Text)) / Convert.ToDouble(XResolution.Text);

                        pulses = Math.Abs(Convert.ToInt32(temp));

                        MyStaticValues.XcompletedPosition = Convert.ToDouble(DistanceX.Text);

                        if (EnableComments.IsChecked == true)
                        {
                            pthatoutput.Text = pthatoutput.Text + "; X position after move " + String.Format("{0:0000.00000}", MyStaticValues.XcompletedPosition) + "mm" + "\n";
                        }

                        pthatoutput.Text = pthatoutput.Text + "B00CX" + String.Format("{0:000000.000}", XHZresult.Text) + String.Format("{0:0000000000}", pulses) + String.Format("{0:0}", Xdir) + "1" + "1" + String.Format("{0:000}", Convert.ToInt16(rampdivide.Text)) + String.Format("{0:000}", Convert.ToInt16(ramppause.Text)) + "0" + String.Format("{0:0}", Convert.ToInt16(EnablePolarity.Text)) + "*" + "\n";

                        if (EnableComments.IsChecked == true)
                        {
                            pthatoutput.Text = pthatoutput.Text + ";" + "\n";
                        }
                        MyStaticValues.XMainCommandCount.Add(MyStaticValues.XcompletedPosition);
                    }
                }
            }

            if (IncludeY.IsChecked == true)
            {
                //Check if Incremental or Absolute Movement
                if (Abso_Off.IsChecked == true)
                {
                    //Make sure desired travel distance can be divided by resolution, if not correct Distance
                    temp = Convert.ToDouble(DistanceY.Text) / Convert.ToDouble(YResolution.Text);
                    pulses = Math.Abs(Convert.ToInt32(temp));
                    DistanceY.Text = Convert.ToString(pulses * Convert.ToDouble(YResolution.Text));

                    if (Yforward.IsChecked == true)
                    {
                        direc = "Forward";
                        Ydir = (PinY.Text == "1") ? 1 : 0;
                    }
                    else
                    {
                        direc = "Back";
                        Ydir = (PinY.Text == "1") ? 0 : 1;
                    }
                    if (EnableComments.IsChecked == true)
                    {
                        pthatoutput.Text = pthatoutput.Text + "; Set Move Y Axis " + DistanceY.Text + "mm " + direc + "\n";
                    }
                    MyStaticValues.YcompletedPosition = (direc == "Forward") ? MyStaticValues.YcompletedPosition + Convert.ToDouble(DistanceY.Text) : MyStaticValues.YcompletedPosition - Convert.ToDouble(DistanceY.Text);

                    if (EnableComments.IsChecked == true)
                    {
                        pthatoutput.Text = pthatoutput.Text + "; Y position after move " + String.Format("{0:0000.00000}", MyStaticValues.YcompletedPosition) + "mm" + "\n";
                    }

                    pthatoutput.Text = pthatoutput.Text + "B00CY" + String.Format("{0:000000.000}", YHZresult.Text) + String.Format("{0:0000000000}", pulses) + String.Format("{0:0}", Ydir) + "1" + "1" + String.Format("{0:000}", Convert.ToInt16(rampdivide.Text)) + String.Format("{0:000}", Convert.ToInt16(ramppause.Text)) + "0" + String.Format("{0:0}", Convert.ToInt16(EnablePolarity.Text)) + "*" + "\n";
                    if (EnableComments.IsChecked == true)
                    {
                        pthatoutput.Text = pthatoutput.Text + ";" + "\n";
                    }
                    MyStaticValues.YMainCommandCount.Add(MyStaticValues.YcompletedPosition);
                }
                else //It is absolute
                {
                    //Check if target position is greater than current position
                    if (Convert.ToDouble(DistanceY.Text) >= MyStaticValues.YcompletedPosition)
                    {
                        //Greater
                        if (Convert.ToDouble(DistanceY.Text) != MyStaticValues.YcompletedPosition)
                        {
                            direc = "Forward";
                            Ydir = (PinY.Text == "1") ? 1 : 0;

                            if (EnableComments.IsChecked == true)
                            {
                                pthatoutput.Text = pthatoutput.Text + "; Set Move Y Axis " + String.Format("{0:0000.00000}", Convert.ToDouble(DistanceY.Text) - MyStaticValues.YcompletedPosition) + "mm " + direc + "\n";
                            }

                            temp = (Convert.ToDouble(DistanceY.Text) - MyStaticValues.YcompletedPosition) / Convert.ToDouble(YResolution.Text);

                            pulses = Math.Abs(Convert.ToInt32(temp));

                            MyStaticValues.YcompletedPosition = Convert.ToDouble(DistanceY.Text);

                            if (EnableComments.IsChecked == true)
                            {
                                pthatoutput.Text = pthatoutput.Text + "; Y position after move " + String.Format("{0:0000.00000}", MyStaticValues.YcompletedPosition) + "mm" + "\n";
                            }

                            pthatoutput.Text = pthatoutput.Text + "B00CY" + String.Format("{0:000000.000}", YHZresult.Text) + String.Format("{0:0000000000}", pulses) + String.Format("{0:0}", Ydir) + "1" + "1" + String.Format("{0:000}", Convert.ToInt16(rampdivide.Text)) + String.Format("{0:000}", Convert.ToInt16(ramppause.Text)) + "0" + String.Format("{0:0}", Convert.ToInt16(EnablePolarity.Text)) + "*" + "\n";

                            if (EnableComments.IsChecked == true)
                            {
                                pthatoutput.Text = pthatoutput.Text + ";" + "\n";
                            }
                            MyStaticValues.YMainCommandCount.Add(MyStaticValues.YcompletedPosition);
                        }
                    }
                    else
                    {
                        direc = "Back";
                        Ydir = (PinY.Text == "1") ? 0 : 1;

                        if (EnableComments.IsChecked == true)
                        {
                            pthatoutput.Text = pthatoutput.Text + "; Set Move Y Axis " + String.Format("{0:0000.00000}", MyStaticValues.YcompletedPosition - Convert.ToDouble(DistanceY.Text)) + "mm " + direc + "\n";
                        }

                        temp = (MyStaticValues.YcompletedPosition - Convert.ToDouble(DistanceY.Text)) / Convert.ToDouble(YResolution.Text);

                        pulses = Math.Abs(Convert.ToInt32(temp));

                        MyStaticValues.YcompletedPosition = Convert.ToDouble(DistanceY.Text);

                        if (EnableComments.IsChecked == true)
                        {
                            pthatoutput.Text = pthatoutput.Text + "; Y position after move " + String.Format("{0:0000.00000}", MyStaticValues.YcompletedPosition) + "mm" + "\n";
                        }

                        pthatoutput.Text = pthatoutput.Text + "B00CY" + String.Format("{0:000000.000}", YHZresult.Text) + String.Format("{0:0000000000}", pulses) + String.Format("{0:0}", Ydir) + "1" + "1" + String.Format("{0:000}", Convert.ToInt16(rampdivide.Text)) + String.Format("{0:000}", Convert.ToInt16(ramppause.Text)) + "0" + String.Format("{0:0}", Convert.ToInt16(EnablePolarity.Text)) + "*" + "\n";

                        if (EnableComments.IsChecked == true)
                        {
                            pthatoutput.Text = pthatoutput.Text + ";" + "\n";
                        }
                        MyStaticValues.YMainCommandCount.Add(MyStaticValues.YcompletedPosition);
                    }
                }
            }

            if (IncludeZ.IsChecked == true)
            {
                //Check if Incremental or Absolute Movement
                if (Abso_Off.IsChecked == true)
                {
                    //Make sure desired travel distance can be divided by resolution, if not correct Distance
                    temp = Convert.ToDouble(DistanceZ.Text) / Convert.ToDouble(ZResolution.Text);
                    pulses = Math.Abs(Convert.ToInt32(temp));
                    DistanceZ.Text = Convert.ToString(pulses * Convert.ToDouble(ZResolution.Text));

                    if (Zup.IsChecked == true)
                    {
                        direc = "Up";
                        Zdir = (PinZ.Text == "1") ? 1 : 0;
                    }
                    else
                    {
                        direc = "Down";
                        Zdir = (PinZ.Text == "1") ? 0 : 1;
                    }
                    if (EnableComments.IsChecked == true)
                    {
                        pthatoutput.Text = pthatoutput.Text + "; Set Move Z Axis " + DistanceZ.Text + "mm " + direc + "\n";
                    }
                    MyStaticValues.ZcompletedPosition = (direc == "Up") ? MyStaticValues.ZcompletedPosition + Convert.ToDouble(DistanceZ.Text) : MyStaticValues.ZcompletedPosition - Convert.ToDouble(DistanceZ.Text);
                    if (EnableComments.IsChecked == true)
                    {
                        pthatoutput.Text = pthatoutput.Text + "; Z position after move " + String.Format("{0:0000.00000}", MyStaticValues.ZcompletedPosition) + "mm" + "\n";
                    }

                    pthatoutput.Text = pthatoutput.Text + "B00CZ" + String.Format("{0:000000.000}", ZHZresult.Text) + String.Format("{0:0000000000}", pulses) + String.Format("{0:0}", Zdir) + "1" + "1" + String.Format("{0:000}", Convert.ToInt16(rampdivide.Text)) + String.Format("{0:000}", Convert.ToInt16(ramppause.Text)) + "0" + String.Format("{0:0}", Convert.ToInt16(EnablePolarity.Text)) + "*" + "\n";
                    if (EnableComments.IsChecked == true)
                    {
                        pthatoutput.Text = pthatoutput.Text + ";" + "\n";
                    }
                    MyStaticValues.ZMainCommandCount.Add(MyStaticValues.ZcompletedPosition);
                }
                else //It is absolute
                {
                    //Check if target position is greater than current position
                    if (Convert.ToDouble(DistanceZ.Text) >= MyStaticValues.ZcompletedPosition)
                    {
                        if (Convert.ToDouble(DistanceZ.Text) != MyStaticValues.ZcompletedPosition)
                        {   //Greater
                            direc = "Up";
                            Zdir = (PinZ.Text == "1") ? 1 : 0;

                            if (EnableComments.IsChecked == true)
                            {
                                pthatoutput.Text = pthatoutput.Text + "; Set Move Z Axis " + String.Format("{0:0000.00000}", Convert.ToDouble(DistanceZ.Text) - MyStaticValues.ZcompletedPosition) + "mm " + direc + "\n";
                            }

                            temp = (Convert.ToDouble(DistanceZ.Text) - MyStaticValues.ZcompletedPosition) / Convert.ToDouble(ZResolution.Text);

                            pulses = Math.Abs(Convert.ToInt32(temp));

                            MyStaticValues.ZcompletedPosition = Convert.ToDouble(DistanceZ.Text);

                            if (EnableComments.IsChecked == true)
                            {
                                pthatoutput.Text = pthatoutput.Text + "; Z position after move " + String.Format("{0:0000.00000}", MyStaticValues.ZcompletedPosition) + "mm" + "\n";
                            }

                            pthatoutput.Text = pthatoutput.Text + "B00CZ" + String.Format("{0:000000.000}", ZHZresult.Text) + String.Format("{0:0000000000}", pulses) + String.Format("{0:0}", Zdir) + "1" + "1" + String.Format("{0:000}", Convert.ToInt16(rampdivide.Text)) + String.Format("{0:000}", Convert.ToInt16(ramppause.Text)) + "0" + String.Format("{0:0}", Convert.ToInt16(EnablePolarity.Text)) + "*" + "\n";

                            if (EnableComments.IsChecked == true)
                            {
                                pthatoutput.Text = pthatoutput.Text + ";" + "\n";
                            }
                            MyStaticValues.ZMainCommandCount.Add(MyStaticValues.ZcompletedPosition);
                        }
                    }
                    else
                    {
                        direc = "Down";
                        Zdir = (PinZ.Text == "1") ? 0 : 1;

                        if (EnableComments.IsChecked == true)
                        {
                            pthatoutput.Text = pthatoutput.Text + "; Set Move Z Axis " + String.Format("{0:0000.00000}", MyStaticValues.ZcompletedPosition - Convert.ToDouble(DistanceZ.Text)) + "mm " + direc + "\n";
                        }

                        temp = (MyStaticValues.ZcompletedPosition - Convert.ToDouble(DistanceZ.Text)) / Convert.ToDouble(ZResolution.Text);

                        pulses = Math.Abs(Convert.ToInt32(temp));

                        MyStaticValues.ZcompletedPosition = Convert.ToDouble(DistanceZ.Text);

                        if (EnableComments.IsChecked == true)
                        {
                            pthatoutput.Text = pthatoutput.Text + "; Z position after move " + String.Format("{0:0000.00000}", MyStaticValues.ZcompletedPosition) + "mm" + "\n";
                        }

                        pthatoutput.Text = pthatoutput.Text + "B00CZ" + String.Format("{0:000000.000}", ZHZresult.Text) + String.Format("{0:0000000000}", pulses) + String.Format("{0:0}", Zdir) + "1" + "1" + String.Format("{0:000}", Convert.ToInt16(rampdivide.Text)) + String.Format("{0:000}", Convert.ToInt16(ramppause.Text)) + "0" + String.Format("{0:0}", Convert.ToInt16(EnablePolarity.Text)) + "*" + "\n";

                        if (EnableComments.IsChecked == true)
                        {
                            pthatoutput.Text = pthatoutput.Text + ";" + "\n";
                        }
                        MyStaticValues.ZMainCommandCount.Add(MyStaticValues.ZcompletedPosition);
                    }
                }
            }

            if (IncludeE.IsChecked == true)
            {
                //Check if Incremental or Absolute Movement
                if (Abso_Off.IsChecked == true)
                {
                    //Make sure desired travel distance can be divided by resolution, if not correct Distance
                    temp = Convert.ToDouble(DistanceE.Text) / Convert.ToDouble(EResolution.Text);
                    pulses = Math.Abs(Convert.ToInt32(temp));
                    DistanceE.Text = Convert.ToString(pulses * Convert.ToDouble(EResolution.Text));

                    if (Eforward.IsChecked == true)
                    {
                        direc = "Forward";
                        Edir = (PinE.Text == "1") ? 1 : 0;
                    }
                    else
                    {
                        direc = "Back";
                        Edir = (PinE.Text == "1") ? 0 : 1;
                    }

                    if (EnableComments.IsChecked == true)
                    {
                        pthatoutput.Text = pthatoutput.Text + "; Set Move E Axis " + DistanceE.Text + "mm " + direc + "\n";
                    }

                    MyStaticValues.EcompletedPosition = (direc == "Forward") ? MyStaticValues.EcompletedPosition + Convert.ToDouble(DistanceE.Text) : MyStaticValues.EcompletedPosition - Convert.ToDouble(DistanceE.Text);

                    if (EnableComments.IsChecked == true)
                    {
                        pthatoutput.Text = pthatoutput.Text + "; E position after move " + String.Format("{0:0000.00000}", MyStaticValues.EcompletedPosition) + "mm" + "\n";
                    }

                    pthatoutput.Text = pthatoutput.Text + "B00CE" + String.Format("{0:000000.000}", EHZresult.Text) + String.Format("{0:0000000000}", pulses) + String.Format("{0:0}", Edir) + "1" + "1" + String.Format("{0:000}", Convert.ToInt16(rampdivide.Text)) + String.Format("{0:000}", Convert.ToInt16(ramppause.Text)) + "0" + String.Format("{0:0}", Convert.ToInt16(EnablePolarity.Text)) + "*" + "\n";

                    if (EnableComments.IsChecked == true)
                    {
                        pthatoutput.Text = pthatoutput.Text + ";" + "\n";
                    }

                    MyStaticValues.EMainCommandCount.Add(MyStaticValues.EcompletedPosition);
                }
                else
                {
                    //It is absolute
                    //Check if target position is greater than current position
                    if (Convert.ToDouble(DistanceE.Text) >= MyStaticValues.EcompletedPosition)
                    {
                        if (Convert.ToDouble(DistanceE.Text) != MyStaticValues.EcompletedPosition)
                        {
                            //Greater

                            direc = "Forward";
                            Edir = (PinE.Text == "1") ? 1 : 0;

                            if (EnableComments.IsChecked == true)
                            {
                                pthatoutput.Text = pthatoutput.Text + "; Set Move E Axis " + String.Format("{0:0000.00000}", Convert.ToDouble(DistanceE.Text) - MyStaticValues.EcompletedPosition) + "mm " + direc + "\n";
                            }

                            temp = (Convert.ToDouble(DistanceE.Text) - MyStaticValues.EcompletedPosition) / Convert.ToDouble(EResolution.Text);

                            pulses = Math.Abs(Convert.ToInt32(temp));

                            MyStaticValues.EcompletedPosition = Convert.ToDouble(DistanceE.Text);

                            if (EnableComments.IsChecked == true)
                            {
                                pthatoutput.Text = pthatoutput.Text + "; E position after move " + String.Format("{0:0000.00000}", MyStaticValues.EcompletedPosition) + "mm" + "\n";
                            }

                            pthatoutput.Text = pthatoutput.Text + "B00CE" + String.Format("{0:000000.000}", EHZresult.Text) + String.Format("{0:0000000000}", pulses) + String.Format("{0:0}", Edir) + "1" + "1" + String.Format("{0:000}", Convert.ToInt16(rampdivide.Text)) + String.Format("{0:000}", Convert.ToInt16(ramppause.Text)) + "0" + String.Format("{0:0}", Convert.ToInt16(EnablePolarity.Text)) + "*" + "\n";

                            if (EnableComments.IsChecked == true)
                            {
                                pthatoutput.Text = pthatoutput.Text + ";" + "\n";
                            }
                            MyStaticValues.EMainCommandCount.Add(MyStaticValues.EcompletedPosition);
                        }
                    }
                    else
                    {
                        direc = "Back";
                        Edir = (PinE.Text == "1") ? 0 : 1;

                        if (EnableComments.IsChecked == true)
                        {
                            pthatoutput.Text = pthatoutput.Text + "; Set Move E Axis " + String.Format("{0:0000.00000}", MyStaticValues.EcompletedPosition - Convert.ToDouble(DistanceE.Text)) + "mm " + direc + "\n";
                        }

                        temp = (MyStaticValues.EcompletedPosition - Convert.ToDouble(DistanceE.Text)) / Convert.ToDouble(EResolution.Text);

                        pulses = Math.Abs(Convert.ToInt32(temp));

                        MyStaticValues.EcompletedPosition = Convert.ToDouble(DistanceE.Text);

                        if (EnableComments.IsChecked == true)
                        {
                            pthatoutput.Text = pthatoutput.Text + "; E position after move " + String.Format("{0:0000.00000}", MyStaticValues.EcompletedPosition) + "mm" + "\n";
                        }

                        pthatoutput.Text = pthatoutput.Text + "B00CE" + String.Format("{0:000000.000}", EHZresult.Text) + String.Format("{0:0000000000}", pulses) + String.Format("{0:0}", Edir) + "1" + "1" + String.Format("{0:000}", Convert.ToInt16(rampdivide.Text)) + String.Format("{0:000}", Convert.ToInt16(ramppause.Text)) + "0" + String.Format("{0:0}", Convert.ToInt16(EnablePolarity.Text)) + "*" + "\n";

                        if (EnableComments.IsChecked == true)
                        {
                            pthatoutput.Text = pthatoutput.Text + ";" + "\n";
                        }

                        MyStaticValues.EMainCommandCount.Add(MyStaticValues.EcompletedPosition);
                    }
                }
            }

            if (EnableComments.IsChecked == true)
            {
                pthatoutput.Text = pthatoutput.Text + "; Send Start All command " + "\n";
            }

            pthatoutput.Text = pthatoutput.Text + "B00SA" + "*" + "\n";

            if (EnableComments.IsChecked == true)
            {
                pthatoutput.Text = pthatoutput.Text + ";" + "\n";
            }

            pthatoutput.Focus(FocusState.Keyboard);
            await Task.Delay(1);
            pthatoutput.Select(pthatoutput.Text.Length - 1, pthatoutput.Text.Length - 1);
        }

        private void Abso_Off_Checked(object sender, RoutedEventArgs e)
        {
            //Enable Incremental Controls
            Traveltype.Text = "Travel Distance";
            Xleft.IsEnabled = true;
            Xright.IsEnabled = true;
            Yforward.IsEnabled = true;
            Yback.IsEnabled = true;
            Zup.IsEnabled = true;
            Zdown.IsEnabled = true;
            Eforward.IsEnabled = true;
            EBack.IsEnabled = true;
        }

        private void Abso_On_Checked(object sender, RoutedEventArgs e)
        {
            //Disable Incremental Controls
            Traveltype.Text = "Travel Target";
            Xleft.IsEnabled = false;
            Xright.IsEnabled = false;
            Yforward.IsEnabled = false;
            Yback.IsEnabled = false;
            Zup.IsEnabled = false;
            Zdown.IsEnabled = false;
            Eforward.IsEnabled = false;
            EBack.IsEnabled = false;
        }

        private void ClearWindow_Click(object sender, RoutedEventArgs e)
        {
            pthatoutput.Text = "";
            MyStaticValues.XcompletedPosition = 0; //Reset Stored positions
            MyStaticValues.YcompletedPosition = 0;//Reset Stored positions
            MyStaticValues.ZcompletedPosition = 0;//Reset Stored positions
            MyStaticValues.EcompletedPosition = 0;//Reset Stored positions

            MyStaticValues.XMainCommandCount.Clear(); //Clear the list that holds the completed position for each command.
            MyStaticValues.YMainCommandCount.Clear(); //Clear the list that holds the completed position for each command.
            MyStaticValues.ZMainCommandCount.Clear(); //Clear the list that holds the completed position for each command.
            MyStaticValues.EMainCommandCount.Clear(); //Clear the list that holds the completed position for each command.
        }

        private void AddCommand1_Click(object sender, RoutedEventArgs e)
        {
            AddCommandtoscript();
        }

        private void Store_Click(object sender, RoutedEventArgs e)
        {
            //Stores all positions
            Abso_On.IsChecked = true;
            IncludeX.IsChecked = true;
            IncludeY.IsChecked = true;
            IncludeZ.IsChecked = true;

            DistanceX.Text = XPosition.Text;
            DistanceY.Text = YPosition.Text;
            DistanceZ.Text = ZPosition.Text;

            PickStore.IsEnabled = true;
            PlaceStore.IsEnabled = true;

            AddCommandtoscript();
        }

        //--------------------------------Jog Code----------------------------------//
        //Jog Y Forward has been pressed
        private void Jyf_press(object sender, PointerRoutedEventArgs e)
        {
            //Jog is Enabled
            if (MyStaticValues.Jenable == 1)
            {
                //Set Jog to pressed
                MyStaticValues.Jenable = 0;

                //Determine Axis and Direction
                MyStaticValues.JogAxis = "Yforward";

                //Sets Action as a press
                MyStaticValues.JogAction = "pressed";

                //Resets Catch as it's a press method
                MyStaticValues.YCatch = 0;

                //initialises jog set method
                InitJog();
            }
        }

        //Jog Y Forward has been released
        private void Jyf_release(object sender, PointerRoutedEventArgs e)
        {
            JYFRELEASE();
        }

        private void JYFRELEASE()
        {
            //Y has triggered a release
            if (MyStaticValues.YCatch == 1)
            {
                //Jog is Pressed
                if (MyStaticValues.Jenable == 0)
                {
                    //Jog is Disabled
                    MyStaticValues.Jenable = 2;

                    //Determine Axis and Direction
                    MyStaticValues.JogAxis = "Yforward";

                    //Sets Action as a release
                    MyStaticValues.JogAction = "released";

                    //initialises jog set method
                    InitJog();
                }
            }
            else
            {
                //Set to trigger a release
                MyStaticValues.YCatch = 1;
            }
        }

        //Jog Y Reverse has been pressed
        private void Jyb_press(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.Jenable == 1)
            {
                MyStaticValues.Jenable = 0;
                MyStaticValues.JogAxis = "Yreverse";
                MyStaticValues.JogAction = "pressed";
                MyStaticValues.YCatch = 0;
                InitJog();
            }
        }

        //Jog Y Reverse has been released
        private void Jyb_release(object sender, PointerRoutedEventArgs e)
        {
            JYBRELEASE();
        }

        private void JYBRELEASE()
        {
            if (MyStaticValues.YCatch == 1)
            {
                if (MyStaticValues.Jenable == 0)
                {
                    MyStaticValues.Jenable = 2;
                    MyStaticValues.JogAxis = "Yreverse";
                    MyStaticValues.JogAction = "released";
                    InitJog();
                }
            }
            else
            {
                MyStaticValues.YCatch = 1;
            }
        }

        //Jog X Right has been pressed
        private void Jxr_press(object sender, PointerRoutedEventArgs e)    //@1
        {
            if (MyStaticValues.Jenable == 1)
            {
                MyStaticValues.Jenable = 0;
                MyStaticValues.JogAxis = "Xright";
                MyStaticValues.JogAction = "pressed";
                MyStaticValues.XCatch = 0;
                InitJog();
            }
        }

        //Jog X Right has been released
        private void Jxr_release(object sender, PointerRoutedEventArgs e)    //@6
        {
            JXRRELEASE();
        }

        private void JXRRELEASE()
        {
            if (MyStaticValues.XCatch == 1)
            {
                if (MyStaticValues.Jenable == 0)
                {
                    MyStaticValues.Jenable = 2;
                    MyStaticValues.JogAxis = "Xright";
                    MyStaticValues.JogAction = "released";
                    InitJog();
                }
            }
            else
            {
                MyStaticValues.XCatch = 1;
            }
        }

        //Jog X Left has been pressed
        private void Jxl_press(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.Jenable == 1)
            {
                MyStaticValues.Jenable = 0;
                MyStaticValues.JogAxis = "Xleft";
                MyStaticValues.JogAction = "pressed";
                MyStaticValues.XCatch = 0;
                InitJog();
            }
        }

        //Jog X Left has been released
        private void Jxl_release(object sender, PointerRoutedEventArgs e)
        {
            JXLRELEASE();
        }

        private void JXLRELEASE()
        {
            if (MyStaticValues.XCatch == 1)
            {
                if (MyStaticValues.Jenable == 0)
                {
                    MyStaticValues.Jenable = 2;
                    MyStaticValues.JogAxis = "Xleft";
                    MyStaticValues.JogAction = "released";
                    InitJog();
                }
            }
            else
            {
                MyStaticValues.XCatch = 1;
            }
        }

        //Jog Z Up has been pressed
        private void Jzu_press(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.Jenable == 1)
            {
                MyStaticValues.Jenable = 0;
                MyStaticValues.JogAxis = "Zup";
                MyStaticValues.JogAction = "pressed";
                MyStaticValues.ZCatch = 0;
                InitJog();
            }
        }

        //Jog Z Up has been released
        private void Jzu_release(object sender, PointerRoutedEventArgs e)
        {
            JZURELEASE();
        }

        private void JZURELEASE()
        {
            if (MyStaticValues.ZCatch == 1)
            {
                if (MyStaticValues.Jenable == 0)
                {
                    MyStaticValues.Jenable = 2;
                    MyStaticValues.JogAxis = "Zup";
                    MyStaticValues.JogAction = "released";
                    InitJog();
                }
            }
            else
            {
                MyStaticValues.ZCatch = 1;
            }
        }

        //Jog Z Down has been pressed
        private void Jzd_press(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.Jenable == 1)
            {
                MyStaticValues.Jenable = 0;
                MyStaticValues.JogAxis = "Zdown";
                MyStaticValues.JogAction = "pressed";
                MyStaticValues.ZCatch = 0;
                InitJog();
            }
        }

        //Jog Z Down has been released
        private void Jzd_release(object sender, PointerRoutedEventArgs e)

        {
            JZDRELEASE();
        }

        private void JZDRELEASE()
        {
            if (MyStaticValues.ZCatch == 1)
            {
                if (MyStaticValues.Jenable == 0)
                {
                    MyStaticValues.Jenable = 2;
                    MyStaticValues.JogAxis = "Zdown";
                    MyStaticValues.JogAction = "released";
                    InitJog();
                }
            }
            else
            {
                MyStaticValues.ZCatch = 1;
            }
        }

        //Pointer has exited object Jog X left
        private void Jxl_Exit(object sender, PointerRoutedEventArgs e)
        {
            //Jog has been pressed
            if (MyStaticValues.Jenable == 0)
            {
                //calls X Left release method
                JXLRELEASE();
            }
        }

        //Pointer has exited object Jog X right
        private void Jxr_Exit(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.Jenable == 0)
            {
                JXRRELEASE();
            }
        }

        //Pointer has exited object Jog Y forward
        private void Jyf_Exit(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.Jenable == 0)
            {
                JYFRELEASE();
            }
        }

        //Pointer has exited object Jog Y reverse
        private void Jyb_Exit(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.Jenable == 0)
            {
                JYBRELEASE();
            }
        }

        //Pointer has exited object Jog Z up
        private void Jzu_Exit(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.Jenable == 0)
            {
                JZURELEASE();
            }
        }

        //Pointer has exited object Jog Z down
        private void Jzd_Exit(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.Jenable == 0)
            {
                JZDRELEASE();
            }
        }

        //Method that initialises Jogging
        private void InitJog()
        {
            //Axis Direction
            string Dir = "";

            //Jog Axis
            string JogIndicator = "";

            //Jog Speed (Hertz)
            string JogSpeed = "";

            //determines whether the button is pressed or released
            switch (MyStaticValues.JogAction)
            {
                //Button is pressed
                case "pressed":

                    //determines which axis and direction to start
                    switch (MyStaticValues.JogAxis)
                    {
                        case "Yforward":

                            //sets image to Button Down
                            BitmapImage j = new BitmapImage(new Uri("ms-appx:///Assets/Up_D.png"));
                            Jyf.Source = j;

                            //sets the axis indicator
                            JogIndicator = "Y";

                            //sets the jog speed for the axis
                            JogSpeed = String.Format("{0:000000.000}", Convert.ToDouble(YHZresult.Text));

                            //sets the pin direction
                            Dir = PinY.Text;

                            break;

                        case "Yreverse":

                            j = new BitmapImage(new Uri("ms-appx:///Assets/Down_D.png"));
                            Jyb.Source = j;
                            JogIndicator = "Y";
                            JogSpeed = String.Format("{0:000000.000}", Convert.ToDouble(YHZresult.Text));
                            Dir = (PinY.Text == "1") ? "0" : "1";

                            break;

                        case "Xleft":

                            j = new BitmapImage(new Uri("ms-appx:///Assets/Left_D.png"));
                            Jxl.Source = j;
                            JogIndicator = "X";
                            JogSpeed = String.Format("{0:000000.000}", Convert.ToDouble(XHZresult.Text));
                            Dir = (PinX.Text == "1") ? "0" : "1";

                            break;

                        case "Xright":

                            j = new BitmapImage(new Uri("ms-appx:///Assets/Right_D.png"));
                            Jxr.Source = j;
                            JogIndicator = "X";
                            JogSpeed = String.Format("{0:000000.000}", Convert.ToDouble(XHZresult.Text));
                            Dir = PinX.Text;

                            break;

                        case "Zup":

                            j = new BitmapImage(new Uri("ms-appx:///Assets/Up_D.png"));
                            Jzu.Source = j;
                            JogIndicator = "Z";
                            JogSpeed = String.Format("{0:000000.000}", Convert.ToDouble(ZHZresult.Text));
                            Dir = PinX.Text;

                            break;

                        case "Zdown":

                            j = new BitmapImage(new Uri("ms-appx:///Assets/Down_D.png"));
                            Jzd.Source = j;
                            JogIndicator = "Z";
                            JogSpeed = String.Format("{0:000000.000}", Convert.ToDouble(ZHZresult.Text));
                            Dir = (PinZ.Text == "1") ? "0" : "1";

                            break;
                    }

                    sendText.Text  = "I00C" + JogIndicator + JogSpeed + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(Jog_RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(Jog_RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";

                     SendDataout();


                    //Determines which axis is selected
                    switch (JogIndicator)
                    {
                        case "X":
                            //stores Xposition
                            MyStaticValues.STORETMP = XPosition.Text;
                            break;

                        case "Y":
                            MyStaticValues.STORETMP = YPosition.Text;
                            break;

                        case "Z":
                            MyStaticValues.STORETMP = ZPosition.Text;
                            break;
                    }

                    break;

                // determines that the button is released
                case "released":

                    //determines which axis and direction to stop
                    switch (MyStaticValues.JogAxis)
                    {
                        case "Yforward":

                            //sets the image to button released
                            BitmapImage j = new BitmapImage(new Uri("ms-appx:///Assets/Up.png"));
                            Jyf.Source = j;

                            //sends out a stop axis command
                            sendText.Text = "I00TY*";
                            SendDataout();

                            break;

                        case "Yreverse":

                            j = new BitmapImage(new Uri("ms-appx:///Assets/Down.png"));
                            Jyb.Source = j;
                            sendText.Text = "I00TY*";
                            SendDataout();

                            break;

                        case "Xleft":

                            j = new BitmapImage(new Uri("ms-appx:///Assets/Left.png"));
                            Jxl.Source = j;
                            sendText.Text = "I00TX*";
                            SendDataout();

                            break;

                        case "Xright":

                            j = new BitmapImage(new Uri("ms-appx:///Assets/Right.png"));
                            Jxr.Source = j;
                            sendText.Text = "I00TX*";
                            SendDataout();

                            break;

                        case "Zup":

                            j = new BitmapImage(new Uri("ms-appx:///Assets/Up.png"));
                            Jzu.Source = j;
                            sendText.Text = "I00TZ*";
                            SendDataout();

                            break;

                        case "Zdown":

                            j = new BitmapImage(new Uri("ms-appx:///Assets/Down.png"));
                            Jzd.Source = j;
                            sendText.Text = "I00TZ*";
                            SendDataout();

                            break;
                    }

                    break;
            }
        }

        //Disables Jog and resets variables
        private void DisableJog()
        {
            MyStaticValues.Jenable = 2;
            MyStaticValues.XCatch = 0;
            MyStaticValues.YCatch = 0;
            MyStaticValues.ZCatch = 0;
        }

        //Enables Jog and resets variables
        private void EnableJog()
        {
            MyStaticValues.Jenable = 1;
            MyStaticValues.XCatch = 0;
            MyStaticValues.YCatch = 0;
            MyStaticValues.ZCatch = 0;
        }

        private void PickStore_Click(object sender, RoutedEventArgs e)
        {
            sendText.Text = "I00A11*";
            SendDataout();

            pthatoutput.Text = pthatoutput.Text + "B00A11*" + "\n";

            //Insert 1 second wait
            pthatoutput.Text = pthatoutput.Text + "B00WW1000*" + "\n";
        }

        private void PlaceStore_Click(object sender, RoutedEventArgs e)
        {
            sendText.Text = "I00A10*";
            SendDataout();

            //Insert 1 second wait
            pthatoutput.Text = pthatoutput.Text + "B00WW1000*" + "\n";

            pthatoutput.Text = pthatoutput.Text + "B00A10*" + "\n";
        }

        private async void GotoZero_Click(object sender, RoutedEventArgs e)
        {
            int Xpulses = 0;
            int Ypulses = 0;
            int Zpulses = 0;
            int Xdir = Convert.ToInt16(PinX.Text);
            int Ydir = Convert.ToInt16(PinY.Text);
            int Zdir = Convert.ToInt16(PinZ.Text);
            MyStaticValues.startbuffersend = 1;
            double temp;
            MyStaticValues.GotoZ = 1;

            //Initialise buffer
            sendText.Text = "H0000*";
            SendDataout();

            waitforinit:

            await Task.Delay(1);

            if (MyStaticValues.NextCommand == 0)
            {
                goto waitforinit;
            }

            //Get Pulse values
            temp = Convert.ToDouble(XPosition.Text) / Convert.ToDouble(XResolution.Text);
            Xpulses = Math.Abs(Convert.ToInt32(temp));

            temp = Convert.ToDouble(YPosition.Text) / Convert.ToDouble(YResolution.Text);
            Ypulses = Math.Abs(Convert.ToInt32(temp));

            temp = Convert.ToDouble(ZPosition.Text) / Convert.ToDouble(ZResolution.Text);
            Zpulses = Math.Abs(Convert.ToInt32(temp));

            //Check if target position is greater than current position
            if (Convert.ToDouble(XPosition.Text) >= 0)
            { //Greater
                Xdir = (PinX.Text == "1") ? 0 : 1;
            }
            else
            { //Is less
                Xdir = (PinX.Text == "1") ? 1 : 0;
            }

            await Task.Delay(10);

            //increment Xset
            MyStaticValues.Xset = MyStaticValues.Xset + 1;

            //Send Buffer Command
            sendText.Text = "B00CX" + String.Format("{0:000000.000}", XHZresult.Text) + String.Format("{0:0000000000}", Xpulses) + String.Format("{0:0}", Xdir) + "1" + "1" + String.Format("{0:000}", Convert.ToInt16(rampdivide.Text)) + String.Format("{0:000}", Convert.ToInt16(ramppause.Text)) + "0" + String.Format("{0:0}", Convert.ToInt16(EnablePolarity.Text)) + "*" + "\n";
            SendDataout();

            //Check if target position is greater than current position
            if (Convert.ToDouble(YPosition.Text) >= 0)
            {
                Ydir = (PinY.Text == "1") ? 0 : 1;
            }
            else
            {
                Ydir = (PinY.Text == "1") ? 1 : 0;
            }

            await Task.Delay(10);
            MyStaticValues.Yset = MyStaticValues.Yset + 1;
            sendText.Text = "B00CY" + String.Format("{0:000000.000}", YHZresult.Text) + String.Format("{0:0000000000}", Ypulses) + String.Format("{0:0}", Ydir) + "1" + "1" + String.Format("{0:000}", Convert.ToInt16(rampdivide.Text)) + String.Format("{0:000}", Convert.ToInt16(ramppause.Text)) + "0" + String.Format("{0:0}", Convert.ToInt16(EnablePolarity.Text)) + "*" + "\n";
            SendDataout();

            //Check if target position is greater than current position
            if (Convert.ToDouble(ZPosition.Text) >= 0)
            {
                Zdir = (PinZ.Text == "1") ? 0 : 1;
            }
            else
            {
                Zdir = (PinZ.Text == "1") ? 1 : 0;
            }

            await Task.Delay(10);
            MyStaticValues.Zset = MyStaticValues.Zset + 1;
            sendText.Text = "B00CZ" + String.Format("{0:000000.000}", ZHZresult.Text) + String.Format("{0:0000000000}", Zpulses) + String.Format("{0:0}", Zdir) + "1" + "1" + String.Format("{0:000}", Convert.ToInt16(rampdivide.Text)) + String.Format("{0:000}", Convert.ToInt16(ramppause.Text)) + "0" + String.Format("{0:0}", Convert.ToInt16(EnablePolarity.Text)) + "*" + "\n";
            SendDataout();

            await Task.Delay(10);

            //Send Start Buffer Command
            sendText.Text = "B00SA*";
            SendDataout();
            await Task.Delay(100);

            MyStaticValues.startbuffersend = 0;
            sendText.Text = "Z0000*";
            SendDataout();
        }
    }
}