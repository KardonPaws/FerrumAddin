﻿using System;
using System.Collections.Generic;
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

namespace FerrumAddinDev.Worksets
{
    /// <summary>
    /// Логика взаимодействия для Workset.xaml
    /// </summary>
    public partial class Workset : Window
    {
        public Workset()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if ((sender as RadioButton).Name == "vid")
            {
                CommandWorksets.byModel = false;
            }
            else
            {
                CommandWorksets.byModel = true;

            }
        }
    }
}
