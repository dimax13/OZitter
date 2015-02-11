using OZitter.Common;
using OZitter.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Input;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// 分割ページのアイテム テンプレートについては、http://go.microsoft.com/fwlink/?LinkId=234234 を参照してください

namespace OZitter
{
    /// <summary>
    /// グループのタイトル、グループ内のアイテムの一覧、および現在選択されているアイテムの
    /// 詳細を表示するページ。
    /// </summary>
    public sealed partial class SplitPage : Page
    {
        private NavigationHelper navigationHelper;
        private ObservableDictionary defaultViewModel = new ObservableDictionary();

        /// <summary>
        /// NavigationHelper は、ナビゲーションおよびプロセス継続時間管理を
        /// 支援するために、各ページで使用します。
        /// </summary>
        public NavigationHelper NavigationHelper
        {
            get { return this.navigationHelper; }
        }

        /// <summary>
        /// これは厳密に型指定されたビュー モデルに変更できます。
        /// </summary>
        public ObservableDictionary DefaultViewModel
        {
            get { return this.defaultViewModel; }
        }

        public SplitPage()
        {
            this.InitializeComponent();

            // Navigation Helper をセットアップします
            this.navigationHelper = new NavigationHelper(this);
            this.navigationHelper.LoadState += navigationHelper_LoadState;
            this.navigationHelper.SaveState += navigationHelper_SaveState;

            // ページに一度に 1 つのペインのみ表示できるようにする論理ページ 
            // ナビゲーション コンポーネントをセットアップします。
            this.navigationHelper.GoBackCommand = new RelayCommand(() => this.GoBack(), () => this.CanGoBack());
            this.itemListView.SelectionChanged += ItemListView_SelectionChanged;

            // 表示するペインの数を 2 つから 1 つに変更する
            // ウィンドウ サイズの変更の待機を開始します
            Window.Current.SizeChanged += Window_SizeChanged;
            this.InvalidateVisualState();

            this.Unloaded += SplitPage_Unloaded;
        }

        /// <summary>
        /// SplitPage がアンロードされるときに SizedChanged イベントからアンフックします。
        /// </summary>
        private void SplitPage_Unloaded(object sender, RoutedEventArgs e)
        {
            Window.Current.SizeChanged -= Window_SizeChanged;
        }

        /// <summary>
        /// このページには、移動中に渡されるコンテンツを設定します。前のセッションからページを
        /// 再作成する場合は、保存状態も指定されます。
        /// </summary>
        /// <param name="sender">
        /// イベントのソース (通常、<see cref="NavigationHelper"/>)
        /// </param>
        /// <param name="e">このページが最初に要求されたときに
        /// <see cref="Frame.Navigate(Type, Object)"/> に渡されたナビゲーション パラメーターと、
        /// 前のセッションでこのページによって保存された状態の辞書を提供する
        /// イベント データ。ページに初めてアクセスするとき、状態は null になります。</param>
        private async void navigationHelper_LoadState(object sender, LoadStateEventArgs e)
        {
            var group = await SampleDataSource.GetGroupAsync((String)e.NavigationParameter);
            this.DefaultViewModel["Group"] = group;
            this.DefaultViewModel["Items"] = group.Items;

            if (e.PageState == null)
            {
                this.itemListView.SelectedItem = null;
                // 新しいページの場合、論理ページ ナビゲーションが使用されている場合を除き、自動的に
                // 最初のアイテムを選択します (以下の論理ページ ナビゲーションの #region を参照)。
                if (!this.UsingLogicalPageNavigation() && this.itemsViewSource.View != null)
                {
                    this.itemsViewSource.View.MoveCurrentToFirst();
                }
            }
            else
            {
                // このページに関連付けられている、前に保存された状態を復元します
                if (e.PageState.ContainsKey("SelectedItem") && this.itemsViewSource.View != null)
                {
                    var selectedItem = await SampleDataSource.GetItemAsync((String)e.PageState["SelectedItem"]);
                    this.itemsViewSource.View.MoveCurrentTo(selectedItem);
                }
            }
        }

        /// <summary>
        /// アプリケーションが中断される場合、またはページがナビゲーション キャッシュから破棄される場合、
        /// このページに関連付けられた状態を保存します。値は、
        /// <see cref="SuspensionManager.SessionState"/> のシリアル化の要件に準拠する必要があります。
        /// </summary>
        /// <param name="navigationParameter">このページが最初に要求されたときに
        /// <see cref="Frame.Navigate(Type, Object)"/> に渡されたパラメーター値。
        /// </param>
        /// <param name="sender">イベントのソース (通常、<see cref="NavigationHelper"/>)</param>
        /// <param name="e">シリアル化可能な状態で作成される空のディクショナリを提供するイベント データ
        ///。</param>
        private void navigationHelper_SaveState(object sender, SaveStateEventArgs e)
        {
            if (this.itemsViewSource.View != null)
            {
                var selectedItem = (Data.SampleDataItem)this.itemsViewSource.View.CurrentItem;
                if (selectedItem != null) e.PageState["SelectedItem"] = selectedItem.UniqueId;
            }
        }

        #region 論理ページ ナビゲーション

        // 分割ページは、ウィンドウの表示スペースを十分に確保できるように設計されています
        // リストと詳細の両方で、一度に 1 つのペインのみが表示されます。
        //
        // これはすべて、2 つの論理ページを表すことができる単一の物理ページと共に実装
        // されます。次のコードを使用すると、ユーザーに違いを感じさせることなく、この目的を達成することが
        // できます。

        private const int MinimumWidthForSupportingTwoPanes = 768;

        /// <summary>
        /// 1 つの論理ページまたは 2 つの論理ページのどちらとしてページが動作するかを判断するために呼び出されます。
        /// </summary>
        /// <returns>ウィンドウを 1 つの論理ページとして使用する場合は true、それ以外は false
        /// false です。</returns>
        private bool UsingLogicalPageNavigation()
        {
            return Window.Current.Bounds.Width < MinimumWidthForSupportingTwoPanes;
        }

        /// <summary>
        /// ウィンドウのサイズが変更されたときに呼び出されます
        /// </summary>
        /// <param name="sender">現在のウィンドウ</param>
        /// <param name="e">ウィンドウの新しいサイズを説明するイベント データ</param>
        private void Window_SizeChanged(object sender, Windows.UI.Core.WindowSizeChangedEventArgs e)
        {
            this.InvalidateVisualState();
        }

        /// <summary>
        /// リスト内のアイテムが選択されたときに呼び出されます。
        /// </summary>
        /// <param name="sender">選択されたアイテムを表示する GridView。</param>
        /// <param name="e">選択がどのように変更されたかを説明するイベント データ。</param>
        private void ItemListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 選択内の変更によって現在の論理ページ内の該当箇所が変更されるため、
            // 論理ページ ナビゲーションが有効な場合にビューステートを無効にします。
            // アイテムが選択されている場合、アイテム リストの表示から、選択されたアイテムの詳細情報の表示に
            // 変更される効果があります。選択がクリアされると、逆に、アイテム リストが
            // 表示されます。
            if (this.UsingLogicalPageNavigation()) this.InvalidateVisualState();
        }

        private bool CanGoBack()
        {
            if (this.UsingLogicalPageNavigation() && this.itemListView.SelectedItem != null)
            {
                return true;
            }
            else
            {
                return this.navigationHelper.CanGoBack();
            }
        }
        private void GoBack()
        {
            if (this.UsingLogicalPageNavigation() && this.itemListView.SelectedItem != null)
            {
                // 論理ページ ナビゲーションが有効で、アイテムが選択され、そのアイテムの
                // 詳細情報が現在表示されています。選択をクリアすると、アイテム リストに
                // 戻ります。ユーザーの立場から見ると、これは、論理的には前に戻る
                // 動作です。
                this.itemListView.SelectedItem = null;
            }
            else
            {
                this.navigationHelper.GoBack();
            }
        }

        private void InvalidateVisualState()
        {
            var visualState = DetermineVisualState();
            VisualStateManager.GoToState(this, visualState, false);
            this.navigationHelper.GoBackCommand.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// アプリケーションのビューステートに対応する表示状態の名前を確認するために
        /// 呼び出されます。
        /// </summary>
        /// <returns>目的の表示状態の名前。これは、縦向きビューおよびスナップ 
        /// ビューでアイテムが選択され、_Detail というサフィックスが追加されたこの追加の論理ページが
        /// 存在している場合を除き、ビューステートの名前と同じです。</returns>
        private string DetermineVisualState()
        {
            if (!UsingLogicalPageNavigation())
                return "PrimaryView";

            // ビュー ステートが変更されたときに [戻る] が有効にされている状態を更新します
            var logicalPageBack = this.UsingLogicalPageNavigation() && this.itemListView.SelectedItem != null;

            return logicalPageBack ? "SinglePane_Detail" : "SinglePane";
        }

        #endregion

        #region NavigationHelper の登録

        /// このセクションに示したメソッドは、NavigationHelper がページの
        /// ナビゲーション メソッドに応答できるようにするためにのみ使用します。
        /// 
        /// ページ固有のロジックは、
        /// <see cref="GridCS.Common.NavigationHelper.LoadState"/>
        /// および <see cref="GridCS.Common.NavigationHelper.SaveState"/> のイベント ハンドラーに配置する必要があります。
        /// LoadState メソッドでは、前のセッションで保存されたページの状態に加え、
        /// ナビゲーション パラメーターを使用できます。

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            navigationHelper.OnNavigatedTo(e);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            navigationHelper.OnNavigatedFrom(e);
        }

        #endregion
    }
}