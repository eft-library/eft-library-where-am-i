using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using where_am_i.ViewModels;

namespace where_am_i.Views
{
    /// <summary>
    /// ConfirmedUserView.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class ConfirmedUserView : Window
    {
        public ConfirmedUserView(string email)
        {
            InitializeComponent();
            DataContext = new ConfirmedUserViewModel(email);
        }
    }
}
