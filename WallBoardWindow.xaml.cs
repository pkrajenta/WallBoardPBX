using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WallBoardPBX
{
    public class Queue
    {
        public string? Number { get; set; }
        public string? Max { get; set; }
        public string? Strategy { get; set; }
        public string? Calls { get; set; }
        public string? Holdtime { get; set; }
        public string? TalkTime { get; set; }
        public string? Completed { get; set; }
        public string? Abandoned { get; set; }
        public string? ServiceLevel { get; set; }

    }
    public class QueueMember
    {
        public string? Name { get; set; }
        public string? Number { get; set; }
        public string? Membership { get; set; }
        public string? CallsTaken { get; set; }
        public string? LastCall { get; set; }
        public string? InCall { get; set; }
        public string? Status { get; set; }
        public string? Paused { get; set; }

    }

    public partial class WallBoardWindow : Window
    {
    
        public WallBoardWindow()
        {
            InitializeComponent();
            ShrinkWindow();
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            ValidateCredentialsThenConnect(e);
        }
        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            Disconnect();
        }

        string AMIUserName = "";
        string AMIPassword = "";
        string PBXIPAddress = "";
        string QueueNumber = "";

        TcpClient client = new();

        public DispatcherTimer timer = new();
        int tickCount = 0;

        string? line = "";
        string AMIprompt = "";
        byte[]? sendData;

        public ObservableCollection<Queue> queue = new();
        public ObservableCollection<QueueMember> queueMembers = new();

        readonly Brush greenBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 180, 0));
        readonly Brush whiteBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255));

        //funkcja używająca TcpClient aby uzyskać połączenie z Asterisk Manager Interface (AMI)
        //przy użyciu protokołu telnet, port 5038
        private async void Connect()
        {                                 
            
            string erMsgAMI = "Połączenie z AMI nie mogło zostać nawiązane.";
            string erTitleAMI = "Nie udało się ustanowić połączenia.";
            string erMsgQueue = "Kolejka o danym numerze nie jest zdeklarowana w wybranym systemie.";
            string erTitleQueue = "Nie udało się uzyskać informacji na temat kolejki.";
            try
            {
                AMIUserName = LoginTextBox.Text;
                AMIPassword = PasswordTextBox.Password;
                PBXIPAddress = IPTextBox.Text;
                QueueNumber = NumberTextBox.Text;

                LoginTextBox.IsReadOnly = true;
                PasswordTextBox.IsEnabled = false;
                IPTextBox.IsReadOnly = true;
                NumberTextBox.IsReadOnly = true;
                ConnectButton.IsEnabled = false;

                Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

                await client.ConnectAsync(PBXIPAddress, 5038);                  

                NetworkStream writeStream = client.GetStream();
                var readStream = new StreamReader(client.GetStream(), Encoding.ASCII);

                sendData = Encoding.ASCII.GetBytes("Action: Login\nUsername:" + AMIUserName + "\nSecret:" + AMIPassword + "\n\n");      //wysyłana jest komenda pozwalająca na zalogowanie
                writeStream.Write(sendData, 0, sendData.Length);                                                                        //poświadczeniami AMI user
                
                while ((line = readStream.ReadLine()) != "")
                    if (line != null)
                        AMIprompt += line + "\n";

                if (!AMIprompt.Contains("Authentication accepted"))
                {
                    FailedToConnect(erMsgAMI, erTitleAMI);
                    return;
                }

                sendData = Encoding.ASCII.GetBytes("Action: Events\nEventmask: off\n\n");
                writeStream.Write(sendData, 0, sendData.Length);

                sendData = Encoding.ASCII.GetBytes("Action: QueueStatus\nQueue: " + QueueNumber + "\n\n");          //komenda zwracająca informacje o kolejce o podanym numerze
                writeStream.Write(sendData, 0, sendData.Length);

                do
                {
                    if (line != null)
                    {
                        line = readStream.ReadLine();
                        AMIprompt += line + "\n";
                    } 
                }
                while (line !=null && !line.Contains("ListItems"));

                if (line != null && line.Contains('0'))
                {
                    FailedToConnect(erMsgQueue, erTitleQueue);
                    return;
                }

                EnlargeWindow();

                PasswordTextBox.IsEnabled = false;
                PasswordTextBox.Password = "";
                LoginStatusLabel.Content = "Zalogowano";
                LoginStatusLabel.Foreground = greenBrush;

                ConnectButton.Visibility = Visibility.Collapsed;
                DisconnectButton.IsEnabled = true;
                DisconnectButton.Visibility = Visibility.Visible;

                AgentDataGridLabel.Visibility = Visibility.Visible;
                AgentDataGrid.Visibility = Visibility.Visible;
                QueueDataGridLabel.Visibility = Visibility.Visible;
                QueueDataGrid.Visibility = Visibility.Visible;
                QueueDataGrid.ItemsSource = queue;
                AgentDataGrid.ItemsSource = queueMembers;

                Mouse.OverrideCursor = System.Windows.Input.Cursors.Arrow;

                queue.Clear();
                queueMembers.Clear();
                QueueDataGrid.Items.Refresh();
                AgentDataGrid.Items.Refresh();

                timer.Interval = TimeSpan.FromSeconds(1);
                timer.Start();
                timer.Tick += Timer_Tick!;
            }
            catch
            {
                FailedToConnect(erMsgAMI, erTitleAMI);
                return;
            }

        }

        //funkcja walidująca poprawność wprowadzonych poświadczeń, jeśli są poprawne przywołuje funkcję Connect()
        private void ValidateCredentialsThenConnect(RoutedEventArgs e)
        {
            bool emptyIPBox = false;
            bool emptyNumberBox = false;
            bool invalidIP = false;
            bool invalidNumber = false;
            string errorMessage = "";
            string IPBoxEmptyMessage = "Pole 'Adres serwera IP' nie może być puste";
            string IPBoxInvalidMessage = "Adres IP musi skladać się wyłącznie z cyfr podzielonych na cztery oktety odseparowane kropką, nie większe niż 255.";
            string numberBoxEmptyMessage = "Pole 'Numer kolejki' nie może być puste";
            string numberBoxInvalidMessage = "Numer kolejki może skladać się wyłącznie z cyfr.";

            if (string.IsNullOrEmpty(IPTextBox.Text))
            {
                invalidIP = true;
                emptyIPBox = true;
            }

            if (!IPAddress.TryParse(IPTextBox.Text, out _) || IPTextBox.Text.Length > 15 || IPTextBox.Text.Length < 7)
                invalidIP = true;

            if (string.IsNullOrEmpty(NumberTextBox.Text))
            {
                invalidNumber = true;
                emptyNumberBox = true;
            }

            if (!int.TryParse(NumberTextBox.Text, out _))
                invalidNumber = true;



            if (invalidIP && !invalidNumber)
            {
                e.Handled = true;
                if (emptyIPBox)
                    errorMessage = IPBoxEmptyMessage;
                else
                    errorMessage = IPBoxInvalidMessage;
                MessageBox.Show(errorMessage, "Niepoprawna składnia adresu IP");
                IPTextBox.Text = "";
                PasswordTextBox.Password = "";
            }
            else if (!invalidIP && invalidNumber)
            {
                e.Handled = true;
                if (emptyNumberBox)
                    errorMessage = numberBoxEmptyMessage;
                else
                    errorMessage = numberBoxInvalidMessage;
                MessageBox.Show(errorMessage, "Niepoprawna składnia numeru kolejki");
                NumberTextBox.Text = "";
                PasswordTextBox.Password = "";
            }
            else if (invalidIP && invalidNumber)
            {
                e.Handled = true;
                if (!emptyIPBox && !emptyNumberBox)
                    errorMessage = IPBoxInvalidMessage + "\n" + numberBoxInvalidMessage;
                else if (!emptyIPBox && emptyNumberBox)
                    errorMessage = IPBoxInvalidMessage + "\n" + numberBoxEmptyMessage;
                else if (emptyIPBox && !emptyNumberBox)
                    errorMessage = IPBoxEmptyMessage + "\n" + numberBoxInvalidMessage;
                else if (emptyIPBox && emptyNumberBox)
                    errorMessage = IPBoxEmptyMessage + "\n" + numberBoxEmptyMessage;

                MessageBox.Show(errorMessage, "Niepoprawna składnia adresu IP i numeru kolejki");
                IPTextBox.Text = "";
                NumberTextBox.Text = "";
                PasswordTextBox.Password = "";
            }
            else
            {
                Connect();
            }
        }

        //funkcja kończąca połączenie z AMI
        private void Disconnect()
        {
            ShrinkWindow();
            client.Close();
            client = new TcpClient();

            timer.Stop();
            timer.Tick -= Timer_Tick!;

            AMIUserName = "";
            AMIPassword = "";
            PBXIPAddress = "";
            QueueNumber = "";

            LoginTextBox.Text = "";
            IPTextBox.Text = "";
            NumberTextBox.Text = "";

            LoginTextBox.IsReadOnly = false;
            PasswordTextBox.IsEnabled = true;
            IPTextBox.IsReadOnly = false;
            NumberTextBox.IsReadOnly = false;

            PasswordTextBox.Visibility = Visibility.Visible;
            PasswordLabel.Visibility = Visibility.Visible;
            LoginStatusLabel.Content = "Konto AMI";
            LoginStatusLabel.Foreground = whiteBrush;

            ConnectButton.IsEnabled = true;
            ConnectButton.Visibility = Visibility.Visible;
            DisconnectButton.IsEnabled = false;
            DisconnectButton.Visibility = Visibility.Collapsed;

            AgentDataGridLabel.Visibility = Visibility.Hidden;
            AgentDataGrid.Visibility = Visibility.Hidden;
            QueueDataGridLabel.Visibility = Visibility.Hidden;
            QueueDataGrid.Visibility = Visibility.Hidden;
        }

        //funkcja wywoływana w przypadku błędu lub braku możliwości połączenia z AMI
        private void FailedToConnect(string erMsg, string erTitle)
        {
            MessageBox.Show(erMsg + "\nSprawdź wprowadzone poświadczenia i spróbuj ponownie.", erTitle);
            client.Close();
            client = new TcpClient();
            PasswordTextBox.Password = "";
            LoginTextBox.IsReadOnly = false;
            PasswordTextBox.IsEnabled = true;
            IPTextBox.IsReadOnly = false;
            NumberTextBox.IsReadOnly = false;
            ConnectButton.IsEnabled = true;
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Arrow;
        }

        int paramsLinesRead = 9;
        int memberLinesRead = 16;

        List<string> list = new();
        readonly string[] queueParams = new string[10];
        readonly string[] queueMember = new string[16];
        string[]? arraySplit;
        string[]? promptAfterSplit;

        bool memberSaved = true;
        bool shouldRead;

        DateTime dateTimeLastCall;
        string membership = "";
        string lastCall = "";
        string inCall = "";
        string status = "";
        string paused = "";
        float serviceLevel = 0;
        float holdTime = 0;
        float talkTime = 0;
        float abandoned = 0;
        float completed = 0;

        //funkcja wykonująca się co sekundę - używana do aktualizowania danych w czasie rzeczywistym
        void Timer_Tick(object sender, EventArgs e)
        {
            tickCount++;
            shouldRead = false;
            AMIprompt = "";
            queue.Clear();
            queueMembers.Clear();

            NetworkStream writeStream = client.GetStream();
            var readStream = new StreamReader(client.GetStream(), Encoding.ASCII);

            sendData = Encoding.ASCII.GetBytes("Action: START" + tickCount.ToString() + "\n\n");
            writeStream.Write(sendData, 0, sendData.Length);

            sendData = Encoding.ASCII.GetBytes("Action: QueueStatus\nQueue: " + QueueNumber + "\n\n");
            writeStream.Write(sendData, 0, sendData.Length);

            sendData = Encoding.ASCII.GetBytes("Action: STOP" + tickCount.ToString() + "\n\n");
            writeStream.Write(sendData, 0, sendData.Length);

            do
            {
                line = readStream.ReadLine();
                if (line != null && line.Contains("START" + tickCount.ToString()))
                    shouldRead = true;
                if (shouldRead)
                    AMIprompt += line + "\n";
            }
            while (line != null && !line.Contains("STOP" + tickCount.ToString()));

            promptAfterSplit = AMIprompt.Split(new string[] { "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
            list = promptAfterSplit.ToList();

            paramsLinesRead = 9;
            memberLinesRead = 16;
            
            foreach (string s in list)
            {
                if (s.Equals("Event: QueueParams"))
                    paramsLinesRead = 0;
                if (s.Equals("Event: QueueMember"))
                {
                    memberLinesRead = 0;
                    memberSaved = false;
                }
                if (paramsLinesRead < 9)
                {
                    arraySplit = s.Split(": ");
                    queueParams[paramsLinesRead] = arraySplit[1];
                    paramsLinesRead++;
                }
                if (memberLinesRead < 16)
                {
                    arraySplit = s.Split(": ");
                    queueMember[memberLinesRead] = arraySplit[1];
                    memberLinesRead++;
                }
                if (memberLinesRead == 16 && memberSaved == false)
                {

                    if (queueMember[8] == "0")
                        lastCall = "-";
                    else
                    {
                        dateTimeLastCall = DateTimeOffset.FromUnixTimeSeconds(long.Parse(queueMember[8])).DateTime.ToLocalTime();
                        lastCall = dateTimeLastCall.ToString().Remove(16, 3).Remove(5, 6).Insert(5, "\t");
                    }


                    if (queueMember[5].Equals("static"))
                        membership = "statyczny";
                    else
                        membership = "dynamiczny";

                    if (queueMember[11] == "0")
                        inCall = "nie";
                    else
                        inCall = "tak";

                    switch (queueMember[12])
                    {
                        case "0":
                            status = "nieznany";
                            break;
                        case "1":
                            status = "gotowy";
                            break;
                        case "2":
                            status = "w użyciu";
                            break;
                        case "3":
                            status = "zajęty";
                            break;
                        case "4":
                            status = "invalid";
                            break;
                        case "5":
                            status = "niedostępny";
                            break;
                        case "6":
                            status = "wywoływany";
                            break;
                        case "7":
                            status = "dzwoni";
                            break;
                        case "8":
                            status = "zawieszony";
                            break;
                    }

                    if (queueMember[13] == "0")
                        paused = "nie";
                    else
                        paused = "tak";

                    queueMembers.Add(new QueueMember
                    {
                        Name = queueMember[2],
                        Number = queueMember[3].Replace("Local/", string.Empty)
                                               .Replace("@from-queue/n", string.Empty),
                        Membership = membership,
                        CallsTaken = queueMember[7],
                        LastCall = lastCall,
                        InCall = inCall,
                        Status = status,
                        Paused = paused
                    });
                    memberSaved = true;
                }
            }

            holdTime = float.Parse(queueParams[5]);
            talkTime = float.Parse(queueParams[6]);
            completed = float.Parse(queueParams[7]);
            abandoned = float.Parse(queueParams[8]);
            if (talkTime + holdTime == 0 || completed + abandoned == 0)
                serviceLevel = 100;
            else
                serviceLevel = ((talkTime / (talkTime + holdTime)) + (completed / (completed + abandoned))) / 2 * 100;

            queue.Add(new Queue
            {
                Number = queueParams[1],
                Max = queueParams[2],
                Strategy = queueParams[3],
                Calls = queueParams[4],
                Holdtime = queueParams[5] + " s",
                TalkTime = queueParams[6] + " s",
                Completed = queueParams[7],
                Abandoned = queueParams[8],
                ServiceLevel = serviceLevel.ToString("0") + "%"
            });

            QueueDataGrid.Items.Refresh();
            AgentDataGrid.Items.Refresh();
        }

        //powiększenie okna aplikacji po pomyślnym nawiązaniu połączenia
        private void EnlargeWindow()
        {
            this.ColumnTwo.Width = new GridLength(21, GridUnitType.Star);
            MainGrid.Visibility = Visibility.Visible;
            AgentBorder.Visibility = Visibility.Visible;
            QueueBorder.Visibility = Visibility.Visible;
            AgentDataGrid.Visibility = Visibility.Visible;
            QueueDataGrid.Visibility = Visibility.Visible;
            AgentDataGridLabel.Visibility = Visibility.Visible;
            QueueDataGridLabel.Visibility = Visibility.Visible;
            this.Width = 1300;
            this.Height = 650;
            this.MinWidth = 1300;
            this.MinHeight = 650;
            this.MaxWidth = 1300;
            this.MaxHeight = 650;

        }

        //pomniejszenie okna po zakończeniu połączenia
        private void ShrinkWindow()
        {
            this.ColumnTwo.Width = new GridLength(0, GridUnitType.Star);
            AgentBorder.Visibility = Visibility.Collapsed;
            QueueBorder.Visibility = Visibility.Collapsed;
            AgentDataGrid.Visibility = Visibility.Collapsed;
            QueueDataGrid.Visibility = Visibility.Collapsed;
            AgentDataGridLabel.Visibility = Visibility.Collapsed;
            QueueDataGridLabel.Visibility = Visibility.Collapsed;
            this.Width = 260;
            this.Height = 470;
            this.MinWidth = 260;
            this.MinHeight = 470;
            this.MaxWidth = 260;
            this.MaxHeight = 470;
        }
    }
}
