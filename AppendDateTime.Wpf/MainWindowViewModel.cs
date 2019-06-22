using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using JPB.WPFBase.MVVM.ViewModel;

namespace AppendDateTime.Wpf
{
	public class MainWindowViewModel : AsyncViewModelBase
	{
		public MainWindowViewModel()
		{
			DownloadValues = new ThreadSaveObservableCollection<decimal>();
		}

		private ThreadSaveObservableCollection<decimal> _downloadValues;

		public ThreadSaveObservableCollection<decimal> DownloadValues
		{
			get { return _downloadValues; }
			set
			{
				SendPropertyChanging(() => DownloadValues);
				_downloadValues = value;
				SendPropertyChanged(() => DownloadValues);
			}
		}
	}
}
