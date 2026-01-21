using System;
using System.Collections.ObjectModel;
using System.Management; // Pozostawione dla zgodności typów z MainForm (ManagementScope)
using System.Management.Automation; // Wymaga pakietu NuGet: Microsoft.PowerShell.SDK
using System.Management.Automation.Runspaces;
using System.Text;
using System.Windows.Forms;

namespace PuzzelLibrary.WMI
{
    public static class ComputerInfo
    {
        // Stałe pozostawione dla kompatybilności z MainForm
        public const string pathCIMv2 = @"root\cimv2";
        public const string queryOperatingSystem = "Win32_OperatingSystem";
        public const string queryComputerSystem = "Win32_ComputerSystem";
        public const string queryPnpDevice = "Win32_PnPSignedDriver";
        // ... reszta stałych nie jest już krytyczna w nowym podejściu, ale mogą zostać

        public static int GetProgressValue { get; set; }

        /// <summary>
        /// Główna metoda pobierająca wszystkie informacje jednym skryptem PowerShell.
        /// </summary>
        public static string AllComputerInfo(string hostName)
        {
            StringBuilder sb = new StringBuilder();
            GetProgressValue = 0;

            try
            {
                // Tworzymy połączenie WinRM
                WSManConnectionInfo connectionInfo = new WSManConnectionInfo();
                connectionInfo.ComputerName = hostName;
                connectionInfo.OperationTimeout = 10000; // 10 sekund timeoutu
                connectionInfo.OpenTimeout = 5000;

                using (Runspace runspace = RunspaceFactory.CreateRunspace(connectionInfo))
                {
                    runspace.Open();
                    using (PowerShell ps = PowerShell.Create())
                    {
                        ps.Runspace = runspace;

                        // Skrypt zbierający wszystko naraz (zoptymalizowany dla Win10/11)
                        ps.AddScript(@"
                            $ErrorActionPreference = 'SilentlyContinue'
                            $os = Get-CimInstance Win32_OperatingSystem
                            $cs = Get-CimInstance Win32_ComputerSystem
                            $bios = Get-CimInstance Win32_BIOS
                            $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
                            $mem = Get-CimInstance Win32_PhysicalMemory
                            $disks = Get-CimInstance Win32_LogicalDisk | Where-Object DriveType -eq 3
                            $nets = Get-CimInstance Win32_NetworkAdapterConfiguration | Where-Object IPEnabled -eq $True
                            $products = Get-CimInstance Win32_Product | Select-Object Name, Version -First 10
                            
                            [PSCustomObject]@{
                                HostName = $cs.DNSHostName
                                Domain = $cs.Domain
                                Uptime = $os.LastBootUpTime
                                Model = $cs.Model
                                Manufacturer = $cs.Manufacturer
                                SerialNumber = (Get-CimInstance Win32_ComputerSystemProduct).IdentifyingNumber
                                OS = $os.Caption
                                OSVersion = $os.Version
                                OSArch = $os.OSArchitecture
                                RAMTotal = ($mem | Measure-Object -Property Capacity -Sum).Sum
                                RAMSlots = $mem
                                CPU = $cpu.Name
                                CPUCores = $cpu.NumberOfCores
                                CPUThreads = $cpu.NumberOfLogicalProcessors
                                UserName = $cs.UserName
                                Disks = $disks
                                Networks = $nets
                                BIOSVer = $bios.SMBIOSBIOSVersion
                                BIOSDate = $bios.ReleaseDate
                            }
                        ");

                        GetProgressValue = 5; // Aktualizacja paska postępu (symulacja)

                        var results = ps.Invoke();

                        // Obsługa błędów połączenia/skryptu
                        if (ps.Streams.Error.Count > 0)
                        {
                            sb.AppendLine("Błędy podczas pobierania danych:");
                            foreach (var err in ps.Streams.Error)
                            {
                                sb.AppendLine($"- {err.ToString()}");
                            }
                        }

                        if (results.Count > 0)
                        {
                            dynamic data = results[0]; // Używamy dynamic, aby łatwo dobrać się do właściwości PSObject

                            // Formatowanie wyjścia (w stylu starego raportu)
                            sb.AppendLine($"Nazwa komputera: {data.HostName}");
                            sb.AppendLine("----------------------------------------");
                            sb.AppendLine($"Domena: {data.Domain}");
                            sb.AppendLine("----------------------------------------");

                            // Uptime (konwersja daty)
                            DateTime uptime = DateTime.Now;
                            try { uptime = (DateTime)data.Uptime; } catch { }
                            TimeSpan ts = DateTime.Now - uptime;
                            sb.AppendLine($"Uptime: {ts.Days} dni, {ts.Hours} godz., {ts.Minutes} min.");
                            sb.AppendLine("----------------------------------------");

                            sb.AppendLine($"SN: {data.SerialNumber}");
                            sb.AppendLine($"Model: {data.Model} ({data.Manufacturer})");
                            sb.AppendLine("----------------------------------------");

                            sb.AppendLine($"OS: {data.OS}");
                            sb.AppendLine($"Wersja: {data.OSVersion} ({data.OSArch})");
                            sb.AppendLine("----------------------------------------");

                            // RAM
                            double ramGB = 0;
                            try { ramGB = Math.Round(Convert.ToDouble(data.RAMTotal) / 1024 / 1024 / 1024, 2); } catch { }
                            sb.AppendLine($"Pamięć TOTAL: {ramGB} GB");

                            // Szczegóły RAM (Iteracja po slotach)
                            sb.AppendLine("Sloty RAM:");
                            if (data.RAMSlots != null)
                            {
                                foreach (dynamic slot in data.RAMSlots)
                                {
                                    try
                                    {
                                        double cap = Math.Round(Convert.ToDouble(slot.Capacity) / 1024 / 1024 / 1024, 2);
                                        sb.AppendLine($" - {slot.DeviceLocator}: {cap} GB ({slot.Speed} MHz) {slot.Manufacturer}");
                                    }
                                    catch { }
                                }
                            }
                            sb.AppendLine("----------------------------------------");

                            sb.AppendLine($"CPU: {data.CPU}");
                            sb.AppendLine($"Rdzenie: {data.CPUCores} | Wątki: {data.CPUThreads}");
                            sb.AppendLine("----------------------------------------");

                            sb.AppendLine($"Zalogowany użytkownik: {data.UserName}");
                            sb.AppendLine("----------------------------------------");

                            sb.AppendLine("Dyski:");
                            if (data.Disks != null)
                            {
                                foreach (dynamic d in data.Disks)
                                {
                                    try
                                    {
                                        double free = Math.Round(Convert.ToDouble(d.FreeSpace) / 1024 / 1024 / 1024, 2);
                                        double total = Math.Round(Convert.ToDouble(d.Size) / 1024 / 1024 / 1024, 2);
                                        sb.AppendLine($" [{d.DeviceID}] {d.VolumeName} | Wolne: {free} GB / {total} GB");
                                    }
                                    catch { }
                                }
                            }
                            sb.AppendLine("----------------------------------------");

                            sb.AppendLine("Sieć:");
                            if (data.Networks != null)
                            {
                                foreach (dynamic net in data.Networks)
                                {
                                    sb.AppendLine($" [{net.Description}]");
                                    sb.AppendLine($"  MAC: {net.MACAddress}");
                                    // IPAddress w PS jest często tablicą
                                    string ipStr = "";
                                    try
                                    {
                                        if (net.IPAddress is string) ipStr = net.IPAddress;
                                        else foreach (string ip in net.IPAddress) ipStr += ip + " ";
                                    }
                                    catch { }
                                    sb.AppendLine($"  IP: {ipStr}");
                                }
                            }
                            sb.AppendLine("----------------------------------------");

                            sb.AppendLine($"BIOS: {data.BIOSVer} ({data.BIOSDate})");

                            GetProgressValue = 20; // Koniec
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("BŁĄD KRYTYCZNY POŁĄCZENIA:");
                sb.AppendLine(ex.Message);
                sb.AppendLine("\nUpewnij się, że:");
                sb.AppendLine("1. WinRM jest włączony na komputerze zdalnym (winrm quickconfig).");
                sb.AppendLine("2. Firewall przepuszcza port 5985.");
                sb.AppendLine("3. Masz uprawnienia administratora.");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Zmieniona metoda Connect - teraz sprawdza tylko dostępność WinRM.
        /// Out scope jest pusty, bo nie używamy już WMI, ale MainForm go wymaga.
        /// </summary>
        public static bool wmiConnect(string computerName, string path, out ManagementScope scope)
        {
            // Atrapa dla zachowania zgodności
            scope = new ManagementScope($@"\\{computerName}\root\cimv2");

            // Szybki test portu 5985 (TCP Ping) lub Test-WSMan
            // Używamy prostej metody TCP, żeby było szybciej niż ładowanie PS
            try
            {
                using (var client = new System.Net.Sockets.TcpClient())
                {
                    var result = client.BeginConnect(computerName, 5985, null, null);
                    var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2));
                    if (!success) return false;
                    client.EndConnect(result);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Metoda pobierająca pojedyncze informacje (dla menu kontekstowego).
        /// Przerobiona na PowerShell, aby działała na Win10/11.
        /// </summary>
        public static string GetInfo(string nazwaKomputera, string path, ManagementScope scope, string query, params object[] args)
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                // Tłumaczenie starych zapytań WMI na nazwy klas CIM
                string className = query.Replace("Select * from ", "").Trim();

                WSManConnectionInfo connectionInfo = new WSManConnectionInfo();
                connectionInfo.ComputerName = nazwaKomputera;

                using (Runspace runspace = RunspaceFactory.CreateRunspace(connectionInfo))
                {
                    runspace.Open();
                    using (PowerShell ps = PowerShell.Create())
                    {
                        ps.Runspace = runspace;
                        // Proste pobranie obiektu
                        ps.AddScript($"Get-CimInstance -ClassName {className} | Select-Object *");

                        var results = ps.Invoke();

                        foreach (PSObject obj in results)
                        {
                            foreach (var propName in args)
                            {
                                string prop = propName.ToString();
                                try
                                {
                                    if (obj.Properties[prop] != null && obj.Properties[prop].Value != null)
                                    {
                                        string val = obj.Properties[prop].Value.ToString();

                                        // Specjalna obsługa formatowania (prosta wersja)
                                        if (prop == "Capacity" || prop == "Size" || prop == "FreeSpace")
                                        {
                                            try
                                            {
                                                double gb = Math.Round(Convert.ToDouble(val) / 1024 / 1024 / 1024, 2);
                                                val = $"{gb} GB";
                                            }
                                            catch { }
                                        }

                                        sb.Append(val + "\t");
                                    }
                                }
                                catch { }
                            }
                            sb.AppendLine();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return "Błąd pobierania danych: " + ex.Message;
            }
            return sb.ToString();
        }
    }
}