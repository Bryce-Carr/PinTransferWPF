using Integration;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SQLite;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Xml.Linq;

namespace PinTransferWPF
{
    /// <summary>
    /// Interaction logic for Window1.xaml
    /// </summary>
    public partial class LabwareDefinitionsWindow : Window
    {
        private readonly LabwareManager _labwareManager;

        public LabwareDefinitionsWindow(string connectionString)
        {
            InitializeComponent();
            _labwareManager = new LabwareManager(connectionString);
            LoadLabware();

            Closed += LabwareDefinitionsWindow_Closed;
        }

        private void LabwareDefinitionsWindow_Closed(object sender, EventArgs e)
        {
            //_labwareManager.Close(); // Dispose the LabwareManager, which will handle connection closing
        }
        private void LoadLabware()
        {
            lstLabware.Items.Clear();
            var labwares = _labwareManager.GetAllLabware();
            foreach (var labware in labwares)
            {
                lstLabware.Items.Add($"{labware.Identifier}: {labware.Height}, {labware.NestedHeight}, {labware.IsLowVolume}, {labware.OffsetY}, {labware.Type}");
            }
            foreach (var item in lstLabware.Items)
            {
                string itemString = item.ToString(); // Get the string representation of the item
                if (itemString.Contains(txtPlateIdentifier.Text + ":"))
                {
                    lstLabware.SelectedItem = item; // Set the selected item
                    break; // Exit the loop if you only want to select the first match
                }
            }
        }

        private void btnAdd_Click(object sender, RoutedEventArgs e)
        {
            var plateIdentifier = txtPlateIdentifier.Text.Trim();
            var plateHeightText = txtPlateHeight.Text.Trim();
            var nestedPlateHeightText = txtNestedPlateHeight.Text.Trim();
            var isLowVolume = chkIsLowVolume.IsChecked.Value;
            var offsetYText = txtOffsetY.Text.Trim();
            var type = txtType.Text.Trim();

            if (string.IsNullOrEmpty(plateIdentifier) || string.IsNullOrEmpty(plateHeightText) || string.IsNullOrEmpty(nestedPlateHeightText) || string.IsNullOrEmpty(offsetYText) || string.IsNullOrEmpty(type))
            {
                MessageBox.Show("Please enter values for all fields.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            double plateHeight, nestedPlateHeight, offsetY;
            if (!double.TryParse(plateHeightText, out plateHeight) || !double.TryParse(nestedPlateHeightText, out nestedPlateHeight) || !double.TryParse(offsetYText, out offsetY))
            {
                MessageBox.Show("Please enter valid numeric values", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                _labwareManager.AddLabware(new Labware
                {
                    Identifier = plateIdentifier,
                    Height = plateHeight,
                    NestedHeight = nestedPlateHeight,
                    IsLowVolume = isLowVolume ? 1 : 0,
                    OffsetY = offsetY,
                    Type = type
                });
                //ClearInputs();
                LoadLabware();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnUpdate_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = lstLabware.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selectedItem))
            {
                MessageBox.Show("Please select a labware to update.");
                return;
            }

            var labware = _labwareManager.GetAllLabware().FirstOrDefault(l => $"{l.Identifier}: {l.Height}, {l.NestedHeight}, {l.IsLowVolume}, {l.OffsetY}, {l.Type}" == selectedItem);

            if (labware == null)
            {
                MessageBox.Show("Selected labware not found.");
                return;
            }

            labware.Height = double.Parse(txtPlateHeight.Text.Trim());
            labware.NestedHeight = double.Parse(txtNestedPlateHeight.Text.Trim());
            labware.IsLowVolume = chkIsLowVolume.IsChecked.Value ? 1 : 0;
            labware.OffsetY = double.Parse(txtOffsetY.Text.Trim());
            labware.Type = txtType.Text.Trim();

            try
            {
                _labwareManager.UpdateLabware(labware, txtPlateIdentifier.Text.Trim());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            //ClearInputs();
            LoadLabware();
        }

        private void btnDelete_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = lstLabware.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selectedItem))
            {
                MessageBox.Show("Please select a labware to delete.");
                return;
            }

            var labware = _labwareManager.GetAllLabware().FirstOrDefault(l => $"{l.Identifier}: {l.Height}, {l.NestedHeight}, {l.IsLowVolume}, {l.OffsetY}, {l.Type}" == selectedItem);

            if (labware == null)
            {
                MessageBox.Show("Selected labware not found.");
                return;
            }

            _labwareManager.DeleteLabware(labware);

            LoadLabware();
            ClearInputs();
        }

        private void lstLabware_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstLabware.SelectedItem == null)
            {
                //ClearInputs();
            }
            else
            {
                var selectedItem = lstLabware.SelectedItem.ToString();
                var labware = _labwareManager.GetAllLabware().FirstOrDefault(l => $"{l.Identifier}: {l.Height}, {l.NestedHeight}, {l.IsLowVolume}, {l.OffsetY}, {l.Type}" == selectedItem);

                if (labware != null)
                {
                    txtPlateIdentifier.Text = labware.Identifier;
                    txtPlateHeight.Text = labware.Height.ToString();
                    txtNestedPlateHeight.Text = labware.NestedHeight.ToString();
                    chkIsLowVolume.IsChecked = labware.IsLowVolume == 1; // Convert int to bool
                    txtOffsetY.Text = labware.OffsetY.ToString();
                    txtType.Text = labware.Type.ToString();
                }
            }
        }
        private void ClearInputs()
        {
            txtPlateIdentifier.Clear();
            txtPlateHeight.Clear();
            txtNestedPlateHeight.Clear();
            chkIsLowVolume.IsChecked = false;
            txtOffsetY.Clear();
            txtType.Clear();
        }
    }
}
