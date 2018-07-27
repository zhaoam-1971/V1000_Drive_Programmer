﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ModbusRTU;
using V1000_Param_Prog;
using System.Runtime.InteropServices;
using XL = Microsoft.Office.Interop.Excel;
using System.Data.SqlClient;
using System.Data.OleDb;
using V1000_ModbusRTU;

namespace V1000_Param_Prog
{
    public partial class frmMain : Form
    {

        // Create objects for Modbus RTU data transmission with the V1000
        ModbusRTUMsg OutputMsg = new ModbusRTUMsg();
        ModbusRTUMsg ResponseMsg = new ModbusRTUMsg();
        ModbusRTUMaster Modbus = new ModbusRTUMaster();
        List<byte> SerialMsg = new List<byte>();

        private ProgressEventArgs ProgressArgs = new ProgressEventArgs();

        //string DataDir = "C:\\Users\\steve\\source\\repos\\V1000_Drive_Programmer\\V1000_Drive_Programmer\\data\\";
        string DataDir = "C:\\Users\\sferry\\source\\repos\\V1000_Drive_Programmer\\V1000_Drive_Programmer\\data\\";
        string OLEBaseStr = "Provider=Microsoft.ACE.OLEDB.12.0;Data Source='";
        string OLEEndStr = "';Extended Properties='Excel 12.0 XML;HDR=YES;';";
        string DriveListFile = "DRIVE_LIST.XLSX";
        string dbFileExt = ".XLSX";

        DataTable dtDriveList = new DataTable();
        DataTable dtParamGrpDesc = new DataTable();
        DataTable dtParamList = new DataTable();

        // Create VFD default parameter read objects 
        List<V1000_Param_Data> Param_List = new List<V1000_Param_Data>();
        List<V1000_Param_Data> Param_Mod = new List<V1000_Param_Data>();
        List<V1000_Param_Data> Param_Chng = new List<V1000_Param_Data>();

        #region Old Code


        private void dgVFDParamView_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            
        }

        private void btnModVFD_Click(object sender, EventArgs e)
        {
            if (!bwrkModVFD.IsBusy)
            {
                ProgressArgs.ClearVFDWriteVals();
                bwrkModVFD.RunWorkerAsync();

                // Configure status bar for displaying VFD parameter read progress
                statProgLabel.Text = "VFD Parameter Modification Progress: ";
                statProgLabel.Visible = true;
                statProgress.Visible = true;

                btnVFDMod.Enabled = false; // disable the Modify VFD button while a write is in progress.
            }
        }

        private void bwrkModVFD_DoWork(object sender, DoWorkEventArgs e)
        {
            
            int status = 0;
            V1000_ModbusRTU_Comm comm = new V1000_ModbusRTU_Comm();
            ModbusRTUMsg msg = new ModbusRTUMsg(0x1F);
            ModbusRTUMaster modbus = new ModbusRTUMaster();
            List<ushort> val = new List<ushort>();

            // proceed further only if opening of communication port is successful
            if (comm.OpenCommPort(ref spVFD) == 0x0001)
            {
                ProgressArgs.VFDWrite_Total_Units = Param_Chng.Count;

                for (int i = 0; i < ProgressArgs.VFDWrite_Total_Units; i++)
                {
                    ProgressArgs.VFDWrite_Unit = i;
                    ProgressArgs.VFDWrite_Progress = (byte)(((float)i / ProgressArgs.VFDWrite_Total_Units) * 100);
                    bwrkModVFD.ReportProgress(ProgressArgs.VFDWrite_Progress);
                    if (bwrkModVFD.CancellationPending)
                    {
                        e.Cancel = true;
                        ProgressArgs.VFDWrite_Stat = ProgressEventArgs.Stat_Canceled;
                        bwrkModVFD.ReportProgress(0);
                        return;
                    }

                    msg.Clear();
                    val.Clear();
                    val.Add(Param_Chng[i].ParamVal);
                    msg = modbus.CreateMessage(msg.SlaveAddr, ModbusRTUMaster.WriteReg, Param_Chng[i].RegAddress, 1, val);

                    status = comm.DataTransfer(ref msg, ref spVFD);
                    if (status != 0x0001)
                    {
                        MessageBox.Show("VFD Parameter Update Failure!!");
                        e.Cancel = true;
                        ProgressArgs.VFDWrite_Stat = ProgressEventArgs.Stat_Error;
                        bwrkModVFD.ReportProgress(0);
                        break;
                    }
                }

                if (status == 0x0001)
                {
                    // Update all the progress and status flags
                    ProgressArgs.VFDWrite_Progress = 100;
                    ProgressArgs.VFDWrite_Stat = ProgressEventArgs.Stat_Complete;
                    e.Result = 0x02;

                    // Save the parameter changes in the VFD
                    status = comm.SaveParamChanges(0x1F, ref spVFD);
                    if (status != 0x0001)
                        MessageBox.Show("VFD Modified Parameter Save Failure!!");
                    bwrkModVFD.ReportProgress(100);
                }

                // Close the communication port and report the thread as complete
                comm.CloseCommPort(ref spVFD);
            }
        }

        private void bwrkModVFD_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            
            // clear all the status bar values and set them as invisible
            statProgLabel.Text = "";
            statProgLabel.Visible = false;
            statProgress.Value = 0;
            statProgress.Visible = false;

            btnVFDMod.Enabled = true; // re-enable the VFD read button

            if (ProgressArgs.VFDWrite_Stat == ProgressEventArgs.Stat_Complete)
            {
                Param_Chng.Clear();
                dgvParamViewChng.Rows.Clear();
                btnReadVFD_Click(sender, (EventArgs)e);
            }
            
        }
        #endregion

        public frmMain()
        {
            InitializeComponent();
        }

        private void frmMain_Load(object sender, EventArgs e)
        {
            // Load available serial ports
            foreach (string s in System.IO.Ports.SerialPort.GetPortNames())
            {
                cmbSerialPort.Items.Add(s);
            }

            // select last serial port, by default it seems the add-on port is always last.
            if (cmbSerialPort.Items.Count > 1)
                cmbSerialPort.SelectedIndex = cmbSerialPort.Items.Count - 1;
            else
                cmbSerialPort.SelectedIndex = 0;

            // Get the list of VFDs available and fill the drive list combo box.
            string conn_str = OLEBaseStr + DataDir + DriveListFile + OLEEndStr;
            if (SQLGetTable(conn_str, ref dtDriveList))
            {
                foreach (DataRow dr in dtDriveList.Rows)
                {
                    string str = dr["UL_PARTNUM"].ToString();
                    cmbDriveList.Items.Add(str);
                }
            }

            SetVFDCommBtnEnable(false, false, false, false);
        }

        private void frmMain_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (spVFD.IsOpen)
                spVFD.Close();
        }

        private void cmbSerialPort_SelectedIndexChanged(object sender, EventArgs e)
        {
            spVFD.PortName = cmbSerialPort.GetItemText(cmbSerialPort.SelectedItem);
        }

        private void cmbParamGroup_SelectedIndexChanged(object sender, EventArgs e)
        {
            DataRow row = dtParamGrpDesc.Rows[cmbParamGroup.SelectedIndex];
            int index = Convert.ToInt16(row["LIST_IDX"].ToString());

            dgvParamViewFull.Rows[index].Selected = true;
            dgvParamViewFull.FirstDisplayedScrollingRowIndex = index;

        }

        private void cmbDriveSel_SelectedIndexChanged(object sender, EventArgs e)
        {
            string file, conn_str;
            cmbParamGroup.Items.Clear();
            DataRow row = dtDriveList.Rows[cmbDriveList.SelectedIndex]; ;

            // Get the for parameter list based on the drive selection.
            file = row["PARAM_LIST"].ToString() + dbFileExt;
            conn_str = OLEBaseStr + DataDir + file + OLEEndStr;
            if (SQLGetTable(conn_str, ref dtParamList))
            {
                Param_List.Clear();
                dgvParamViewFull.Rows.Clear();
                
                foreach (DataRow dr in dtParamList.Rows)
                {
                    V1000_Param_Data param = new V1000_Param_Data();
                    V1000SQLtoParam(dr, ref param);
                    Param_List.Add(param);
                    dgvParamViewFull.Rows.Add(new string[]
                        {
                            ("0x" + param.RegAddress.ToString("X4")),
                            param.ParamNum,
                            param.ParamName,
                            param.DefValDisp
                        });
                    dgvParamViewFull.Rows[dtParamList.Rows.IndexOf(dr)].Cells[4].ReadOnly = false;
                }

                SetVFDCommBtnEnable(true, false, false, false);
            }

            // Get the list of parameter groupings available and fill the Parameter group combobox
            file = row["PARAM_GRP_DESC"].ToString() + dbFileExt;
            conn_str = OLEBaseStr + DataDir + file + OLEEndStr;
            
            if (SQLGetTable(conn_str, ref dtParamGrpDesc))
            {
                foreach (DataRow dr in dtParamGrpDesc.Rows)
                {
                    string str = dr["PARAM_GRP"].ToString() + " - " + dr["GRP_DESC"].ToString();
                    cmbParamGroup.Items.Add(str);
                }
                cmbParamGroup.SelectedIndex = 0;
            }
        }

        private void btnReadVFD_Click(object sender, EventArgs e)
        {
            if (!bwrkReadVFDVals.IsBusy)
            {
                dgvParamViewFull.Columns[4].ReadOnly = true;
                Param_Mod.Clear();
                dgvParamViewMod.Rows.Clear();
                ProgressArgs.ClearVFDReadVals();    // Initialize the progress flags for a VFD read
                bwrkReadVFDVals.RunWorkerAsync();   // Start the separate thread for reading the current VFD parameter settings

                // Configure status bar for displaying VFD parameter read progress
                statProgLabel.Text = "VFD Parameter Value Read Progress: ";
                statProgLabel.Visible = true;
                statProgress.Visible = true;

                // disable the VFD communication buttons while a read is in progress.
                SetVFDCommBtnEnable(false, false, false, false);
            }
        }

        private void bwrkReadVFDVals_DoWork(object sender, DoWorkEventArgs e)
        {
            int status = 0;
            V1000_ModbusRTU_Comm comm = new V1000_ModbusRTU_Comm();
            ModbusRTUMsg msg = new ModbusRTUMsg(0x1F);
            ModbusRTUMaster modbus = new ModbusRTUMaster();
            List<ushort> tmp = new List<ushort>();

            // proceed further only if opening of communication port is successful
            if (comm.OpenCommPort(ref spVFD) == 0x0001)
            {
                ProgressArgs.VFDRead_Total_Units = Param_List.Count;

                for (int i = 0; i < ProgressArgs.VFDRead_Total_Units; i++)
                {
                    ProgressArgs.VFDRead_Unit = i;
                    ProgressArgs.VFDRead_Progress = (byte)(((float)i / ProgressArgs.VFDRead_Total_Units) * 100);
                    bwrkReadVFDVals.ReportProgress(ProgressArgs.VFDRead_Progress);
                    if (bwrkReadVFDVals.CancellationPending)
                    {
                        e.Cancel = true;
                        bwrkReadVFDVals.ReportProgress(0);
                        return;
                    }

                    msg.Clear();
                    msg = modbus.CreateMessage(msg.SlaveAddr, ModbusRTUMaster.ReadReg, Param_List[i].RegAddress, 1, tmp);

                    status = comm.DataTransfer(ref msg, ref spVFD);
                    if (status == 0x0001)
                        Param_List[i].ParamVal = msg.Data[0];
                }

                ProgressArgs.VFDRead_Progress = 100;
                ProgressArgs.VFDRead_Stat = 0x02;
                e.Result = 0x02;
                comm.CloseCommPort(ref spVFD);
                bwrkReadVFDVals.ReportProgress(100);
            }
        }

        private void bwrkDGV_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            statProgress.Value = e.ProgressPercentage;
        }

        private void bwrkReadVFDVals_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            // populate the VFD Value column on the VFD Parameter Values datagridview
            for (int i = 0; i < dgvParamViewFull.RowCount; i++)
            {
                dgvParamViewFull.Rows[i].Cells[4].Value = Param_List[i].ParamValDisp;
                dgvParamViewFull.Rows[i].Cells[4].ReadOnly = false;

                // Check if the read value from the VFD differs from the default parameter setting.
                // If it does add it to the modified parameters datagridview.
                if (Param_List[i].ParamVal != Param_List[i].DefVal)
                {
                    // Clone the row with the parameter that differs from the default value and add it to 
                    // the Datagridview for modified parameters. 
                    DataGridViewRow ClonedRow = (DataGridViewRow)dgvParamViewFull.Rows[i].Clone();
                    ClonedRow.DefaultCellStyle.BackColor = Color.White; // don't want a custom color row for this datagridview
                    for (int j = 0; j < dgvParamViewFull.ColumnCount; j++)
                        ClonedRow.Cells[j].Value = dgvParamViewFull.Rows[i].Cells[j].Value;
                    dgvParamViewMod.Rows.Add(ClonedRow);

                    // Turn the VFD Parameter Values datagridview row with the non-default parameter yellow to signify 
                    // that the particular parameter is set to a non-default value
                    dgvParamViewFull.Rows[i].DefaultCellStyle.BackColor = Color.Yellow;
                }
                else
                {
                    // Set the backcolor for the row back to white. This is done because any additional read 
                    // showing that the value is now set to default will force the previously signified as 
                    // changed row back to a default white color.
                    dgvParamViewFull.Rows[i].DefaultCellStyle.BackColor = Color.White;
                }
            }

            // clear all the status bar values and set them as invisible
            statProgLabel.Text = "";
            statProgLabel.Visible = false;
            statProgress.Value = 0;
            statProgress.Visible = false;

            SetVFDCommBtnEnable(true, true, true, true);

        }

        public bool SQLGetTable(string p_ConnStr, ref DataTable p_Tbl, string p_Query = "SELECT * FROM [SHEET1$]")
        {
            bool RetVal = false;

            using (OleDbConnection dbConn = new OleDbConnection(p_ConnStr))
            {
                if (dbConn.State == ConnectionState.Closed)
                {
                    dbConn.Open();
                    if (dbConn.State == ConnectionState.Open)
                    {
                        OleDbDataAdapter da = new OleDbDataAdapter(p_Query, dbConn);
                        DataSet ds = new DataSet();
                        try
                        {
                            da.Fill(ds);
                            p_Tbl.Clear();
                            p_Tbl = ds.Tables[0];
                            if (p_Tbl.Rows.Count > 0)
                                RetVal = true;
                            else
                                RetVal = false;
                        }
                        catch
                        {
                            MessageBox.Show("Database Error!");
                            RetVal = false;
                        }

                        dbConn.Close();

                    }
                    else
                        RetVal = false;
                }
            }
            return RetVal;
        }

        public void V1000SQLtoParam(DataRow p_dr, ref V1000_Param_Data p_Data)
        {
            p_Data.RegAddress = Convert.ToUInt16(p_dr[1].ToString());
            p_Data.ParamNum = p_dr[2].ToString();
            p_Data.ParamName = p_dr[3].ToString();
            p_Data.Multiplier = Convert.ToUInt16(p_dr[5].ToString());
            p_Data.NumBase = Convert.ToByte(p_dr[6].ToString());
            p_Data.Units = p_dr[7].ToString();
            p_Data.DefVal = Convert.ToUInt16(p_dr[4].ToString());
        }

        private void btnVFDReset_Click(object sender, EventArgs e)
        {
            V1000_ModbusRTU_Comm comm = new V1000_ModbusRTU_Comm();
            ModbusRTUMsg msg = new ModbusRTUMsg(0x1F);
            ModbusRTUMaster modbus = new ModbusRTUMaster();
            List<ushort> val = new List<ushort>();

            

            msg.Clear();
            val.Clear();
            val.Add(2220);
            msg = modbus.CreateMessage(msg.SlaveAddr, ModbusRTUMaster.WriteReg, 0x0103, 1, val);

            if (comm.OpenCommPort(ref spVFD) == 0x0001)
            {
                SetVFDCommBtnEnable(false, false, false, false);
                int status = comm.DataTransfer(ref msg, ref spVFD);
                if (status != 0x0001)
                    MessageBox.Show("VFD Parameter Reset to Default Failure!!");
                else
                {
                    // click the Read VFD button to refresh the datagridview for the full parameter 
                    // list and clear the datagridview for the modified parameter list 
                    btnReadVFD_Click(sender, e);
                }
                comm.CloseCommPort(ref spVFD);
                SetVFDCommBtnEnable(true, true, true, true);
            }
        }

        private void dgvParamViewFull_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            int index = e.RowIndex;
            Single chng_val = 0;
            Single def_val;

            // get default and changed cell values in a version that can be compared
            def_val = CelltoSingle((String)dgvParamViewFull.Rows[index].Cells[3].Value, Param_List[index]);
            chng_val = CelltoSingle((String)dgvParamViewFull.Rows[index].Cells[4].Value, Param_List[index]);

            // We multiply the temporary decimal value by the parameters standard multiplier.
            // and we convert the result of the multiplication to a 16-bit register value 
            // that both Modbus RTU and the V1000 require.
            Single temp_float = (chng_val * Param_List[index].Multiplier);
            ushort chng_param_val = (ushort)temp_float;


            // Check and see if the parameter value actually changed. Just double-clicking on the cell and 
            // hitting enter will cause this event to trigger even if the value does not change.
            if (chng_param_val != Param_List[index].ParamVal)
            {
                // Copy the full parameter data to a list that contains scheduled changed values.
                Param_Chng.Add((V1000_Param_Data)Param_List[index].Clone());

                // Overwrite the copied current parameter value with new changed value
                Param_Chng[Param_Chng.Count - 1].ParamVal = chng_param_val;

                // Clone the row with the changed value and add it to the Datagridview for scheduled parameter changes.
                DataGridViewRow ClonedRow = (DataGridViewRow)dgvParamViewFull.Rows[index].Clone();
                for (int i = 0; i < dgvParamViewFull.Rows[index].Cells.Count; i++)
                    ClonedRow.Cells[i].Value = dgvParamViewFull.Rows[index].Cells[i].Value;
                ClonedRow.Cells[4].Value = Param_Chng[Param_Chng.Count - 1].ParamValDisp;
                ClonedRow.DefaultCellStyle.BackColor = Color.White;
                dgvParamViewChng.Rows.Add(ClonedRow);

                // Fix the user entry to be the properly formatted string from any inaccuracies in formatting by the user.
                dgvParamViewFull.Rows[index].Cells[4].Value = Param_Chng[Param_Chng.Count - 1].ParamValDisp;

                // Highlight the scheduled changed parameter in the default parameter and current VFD parameter 
                // in Green-Yellow to signify that a change is scheduled for that particular parameter.
                dgvParamViewFull.Rows[index].DefaultCellStyle.BackColor = Color.GreenYellow;

                // If there is more than one modified parameter enable the Modify VFD Parameters button.
                if (Param_Chng.Count > 0)
                    btnVFDMod.Enabled = true;
            }
            else
            {
            }
        }

        private void SetVFDCommBtnEnable(bool p_ReadEn, bool p_InitEn, bool p_ModEnd, bool p_MonEn)
        {
            btnVFDRead.Enabled = p_ReadEn;
            btnVFDReset.Enabled = p_InitEn;
            btnVFDMod.Enabled = p_ModEnd;
            btnVFDMon.Enabled = p_MonEn;
        }

        private Single CelltoSingle(string p_CellVal, V1000_Param_Data p_Param)
        {
            Single RetVal = 0;

            try
            {
                // First check if the default parameter is a hex value so we can trim off the "0x" from the beginning
                if (p_Param.NumBase == 16)
                {
                    // Now check and see if the value is actually a hex value
                    if ((p_CellVal.IndexOf('x') > 0)) |
                    {
                        ushort temp_val = Convert.ToUInt16(p_CellVal.Substring(2), 16);
                        RetVal = Convert.ToSingle(temp_val);
                    }
                }
                // Otherwise we need to trim off any units from the default cell value
                else
                {
                    int unit_index = p_CellVal.IndexOf(' ');
                    if (unit_index > 0)
                    {
                        p_CellVal = p_CellVal.Substring(0, unit_index);
                        RetVal = Convert.ToSingle(p_CellVal);
                    }
                    else
                        RetVal = Convert.ToSingle(p_CellVal);

                }
            }
            catch
            {
                MessageBox.Show("Entry Error!!");
            }
            return RetVal;
        }
    }



    public class ProgressEventArgs : EventArgs
    {
        // Mode Legend:
        public const byte xlReadMode = 0x01;
        public const byte xlWriteMode = 0x02;
        public const byte VFDReadMode = 0x03;

        public byte Mode_Sel = 0;

        // status legend:
        public const byte Stat_NotRunning = 0x00;
        public const byte Stat_Running = 0x01;
        public const byte Stat_Complete = 0x02;
        public const byte Stat_Canceled = 0x03;
        public const byte Stat_Error = 0xFF;

        public byte   VFDRead_Stat = 0;
        public byte   VFDRead_ErrCode = 0;
        public int    VFDRead_Unit = 0;
        public int    VFDRead_Total_Units = 0;
        public byte   VFDRead_Progress = 0;
        public string VFDRead_ParamNum = "";
        public string VFDRead_ParamName = "";

        public byte VFDWrite_Stat = 0;
        public byte VFDWrite_ErrCode = 0;
        public int VFDWrite_Unit = 0;
        public int VFDWrite_Total_Units = 0;
        public byte VFDWrite_Progress = 0;


        public ProgressEventArgs() { }

        public void ClearAll()
        {
            VFDRead_Stat = 0;
            VFDRead_ErrCode = 0;
            VFDRead_Unit = 0;
            VFDRead_Total_Units = 0;
            VFDRead_Progress = 0;
            VFDRead_ParamNum = "";
            VFDRead_ParamName = "";

            VFDWrite_Stat = 0;
            VFDWrite_ErrCode = 0;
            VFDWrite_Unit = 0;
            VFDWrite_Total_Units = 0;
            VFDWrite_Progress = 0;
    }

        public void ClearVFDReadVals()
        {
            VFDRead_Stat = 0;
            VFDRead_ErrCode = 0;
            VFDRead_Unit = 0;
            VFDRead_Total_Units = 0;
            VFDRead_Progress = 0;
            VFDRead_ParamNum = "";
            VFDRead_ParamName = "";
        }

        public void ClearVFDWriteVals()
        {
            VFDWrite_Stat = 0;
            VFDWrite_ErrCode = 0;
            VFDWrite_Unit = 0;
            VFDWrite_Total_Units = 0;
            VFDWrite_Progress = 0;
        }
    }

}