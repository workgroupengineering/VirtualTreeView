﻿// VirtualTreeView - a TreeView that *actually* allows virtualization
// https://github.com/picrap/VirtualTreeView

namespace VirtualTreeView
{
    using System;
    using System.Collections;
    using System.Collections.ObjectModel;
    using System.Collections.Specialized;
    using System.ComponentModel;
    using System.Linq;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Controls.Primitives;
    using System.Windows.Data;
    using System.Windows.Markup;
    using Collection;
    using Reflection;

    /// <summary>
    /// The VirtualTreeView is a line-by-line virtualized <see cref="TreeView"/>
    /// It simply flattens the hierarchical input content to an <see cref="ItemsControl"/> with <see cref="VirtualizingStackPanel"/>
    /// </summary>
    /// <seealso cref="System.Windows.Controls.ItemsControl" />
    [StyleTypedProperty(Property = nameof(ItemContainerStyle), StyleTargetType = typeof(TreeViewItem))]
    [ContentProperty(nameof(HierarchicalItems))]
    public class VirtualTreeView : ItemsControl
    {
        private readonly ObservableCollection<object> _hierarchicalItemsSource = new ObservableCollection<object>();

        private bool _hierarchicalItemsSourceBound;

        /// <summary>
        /// Gets the hierarchical items.
        /// This is the content setter for hard-coded tree-view items
        /// Which is totally pointless with this control, since its best comes with binding!
        /// </summary>
        /// <value>
        /// The hierarchical items.
        /// </value>
        public IList HierarchicalItems { get; } = new ObservableCollection<object>();

        /// <summary>
        /// Gets or sets a value indicating whether this instance is selection change active.
        /// </summary>
        /// <value>
        /// <c>true</c> if this instance is selection change active; otherwise, <c>false</c>.
        /// </value>
        public bool IsSelectionChangeActive { get; set; }

        /// <summary>
        /// The selected item property
        /// </summary>
        public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
            "SelectedItem", typeof(object), typeof(VirtualTreeView), new PropertyMetadata(default(object)));

        /// <summary>
        /// Gets or sets the selected item.
        /// </summary>
        /// <value>
        /// The selected item.
        /// </value>
        public object SelectedItem
        {
            get { return GetValue(SelectedItemProperty); }
            set { SetValue(SelectedItemProperty, value); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether [optimize item bindings].
        /// </summary>
        /// <value>
        /// <c>true</c> if [optimize item bindings]; otherwise, <c>false</c>.
        /// </value>
        public bool OptimizeItemBindings { get; set; } = true;

        private VirtualTreeViewItemFlatCollection FlatItems { get; }
        private VirtualTreeViewItemsSourceFlatCollection FlatItemsSource { get; set; }

        /// <summary>
        ///     Event fired when <see cref="SelectedItem"/> changes.
        /// </summary>
        public static readonly RoutedEvent SelectedItemChangedEvent
            = EventManager.RegisterRoutedEvent("SelectedItemChanged", RoutingStrategy.Bubble, typeof(RoutedPropertyChangedEventHandler<object>), typeof(VirtualTreeView));

        /// <summary>
        ///     Event fired when <see cref="SelectedItem"/> changes.
        /// </summary>
        [Category("Behavior")]
        public event RoutedPropertyChangedEventHandler<object> SelectedItemChanged
        {
            add { AddHandler(SelectedItemChangedEvent, value); }
            remove { RemoveHandler(SelectedItemChangedEvent, value); }
        }

        /// <summary>
        ///     Called when <see cref="SelectedItem"/> changes.
        ///     Default implementation fires the <see cref="SelectedItemChanged"/> event.
        /// </summary>
        /// <param name="e">Event arguments.</param>
        protected virtual void OnSelectedItemChanged(RoutedPropertyChangedEventArgs<object> e)
        {
            RaiseEvent(e);
        }

        static VirtualTreeView()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(VirtualTreeView), new FrameworkPropertyMetadata(typeof(VirtualTreeView)));
            ItemsSourceProperty.OverrideMetadata(typeof(VirtualTreeView), new FrameworkPropertyMetadata(OnItemsSourceChanged));
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VirtualTreeView"/> class.
        /// </summary>
        public VirtualTreeView()
        {
            FlatItems = new VirtualTreeViewItemFlatCollection(HierarchicalItems, Items);
            HierarchicalItems.IfType<INotifyCollectionChanged>(nc => nc.OnAddRemove(o => o.IfType<VirtualTreeViewItem>(i => i.ParentTreeView = this)));
        }

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var value = (IEnumerable)e.NewValue;
            var @this = (VirtualTreeView)d;
            @this.OnItemsSourceChanged(value);
        }

        private bool _settingSource;

        /// <summary>
        /// Called when ItemsSource is bound.
        /// </summary>
        /// <param name="value">The value.</param>
        private void OnItemsSourceChanged(IEnumerable value)
        {
            if (_settingSource)
                return;

            _hierarchicalItemsSource.Clear();
            _hierarchicalItemsSourceBound = value != null;
            if (_hierarchicalItemsSourceBound)
            {
                // on first binding, create the collection
                if (FlatItemsSource == null)
                {
                    var itemsSource = new ObservableCollection<object>();
                    FlatItemsSource = new VirtualTreeViewItemsSourceFlatCollection(_hierarchicalItemsSource, itemsSource, this);
                    _settingSource = true;
                    // now setting the flat source that the ItemsControl will use
                    ItemsSource = itemsSource;
                    _settingSource = false;
                }
                if (IsLoaded)
                {
                    foreach (var newItem in value)
                        _hierarchicalItemsSource.Add(newItem);
                }
                else
                    Loaded += delegate
                    {
                        foreach (var newItem in value)
                            _hierarchicalItemsSource.Add(newItem);
                    };
            }
        }

        internal void OnExpanded(ItemsControl item)
        {
            if (_hierarchicalItemsSourceBound)
                FlatItemsSource.Expand(item.DataContext);
            else
                FlatItems.Expand(item);
        }

        internal void OnCollapsed(ItemsControl item)
        {
            if (_hierarchicalItemsSourceBound)
                FlatItemsSource.Collapse(item.DataContext);
            else
                FlatItems.Collapse(item);
        }

        private VirtualTreeViewItem _selectedContainer;

        internal void ChangeSelection(object data, VirtualTreeViewItem container, bool selected)
        {
            if (IsSelectionChangeActive)
            {
                return;
            }

            object oldValue = null;
            object newValue = null;
            bool changed = false;

            IsSelectionChangeActive = true;

            try
            {
                if (selected)
                {
                    if (!ReferenceEquals(container, _selectedContainer))
                    {
                        oldValue = SelectedItem;
                        newValue = data;

                        if (_selectedContainer != null)
                        {
                            _selectedContainer.IsSelected = false;
                            _selectedContainer.UpdateContainsSelection(false);
                        }
                        _selectedContainer = container;
                        _selectedContainer.UpdateContainsSelection(true);
                        SelectedItem = data;
                        //UpdateSelectedValue(data);
                        changed = true;
                    }
                }
                else
                {
                    if (ReferenceEquals(container, _selectedContainer))
                    {
                        _selectedContainer.UpdateContainsSelection(false);
                        _selectedContainer = null;
                        SelectedItem = null;

                        oldValue = data;
                        changed = true;
                    }
                }

                if (container.IsSelected != selected)
                    container.IsSelected = selected;
            }
            finally
            {
                IsSelectionChangeActive = false;
            }

            if (changed)
            {
                var e = new RoutedPropertyChangedEventArgs<object>(oldValue, newValue, SelectedItemChangedEvent);
                OnSelectedItemChanged(e);
            }
        }

        /// <summary>
        /// Creates or identifies the element that is used to display the given item.
        /// </summary>
        /// <returns>
        /// The element that is used to display the given item.
        /// </returns>
        protected override DependencyObject GetContainerForItemOverride()
        {
            return new VirtualTreeViewItem();
        }

        /// <summary>
        /// Prepares the specified element to display the specified item.
        /// </summary>
        /// <param name="element">Element used to display the specified item.</param>
        /// <param name="item">Specified item.</param>
        protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
        {
            var treeViewItem = element as VirtualTreeViewItem;
            if (treeViewItem != null)
            {
                treeViewItem.ParentTreeView = this;
                treeViewItem.Depth = GetDepth(treeViewItem);
            }
            base.PrepareContainerForItemOverride(element, item);
        }

        /// <summary>
        /// Gets the depth of the given item.
        /// This is used by binding generated items
        /// </summary>
        /// <param name="treeViewItem">The tree view item.</param>
        /// <returns></returns>
        internal int GetDepth(VirtualTreeViewItem treeViewItem)
        {
            int depth = -1; // starting from -1 here, cause the dataContext below will be non-null at least once
            for (var dataContext = treeViewItem.DataContext; dataContext != null; dataContext = FlatItemsSource.GetParent(dataContext))
                depth++;
            return depth;
        }

        private ItemsControlItemPropertyReader<bool> _isExpandedPropertyReader;

        internal bool IsExpanded(object item)
        {
            if (_isExpandedPropertyReader == null)
                _isExpandedPropertyReader = new ItemsControlItemPropertyReader<bool>(this, VirtualTreeViewItem.IsExpandedProperty, allowSourceProperties: OptimizeItemBindings);
            return _isExpandedPropertyReader.Get(item);
        }


        private ItemsControlItemPropertyReader<IEnumerable> _childrenPropertyReader;

        internal IEnumerable GetChildren(object item)
        {
            if (_childrenPropertyReader == null)
                _childrenPropertyReader = new ItemsControlItemPropertyReader<IEnumerable>(this, VirtualTreeViewItem.ItemsSourceProperty, allowSourceProperties: OptimizeItemBindings);
            return _childrenPropertyReader.Get(item);
        }
    }
}
