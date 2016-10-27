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
    using System.Windows.Media;
    using Collection;
    using Reflection;

    [StyleTypedProperty(Property = nameof(ItemContainerStyle), StyleTargetType = typeof(TreeViewItem))]
    [ContentProperty(nameof(HierarchicalItems))]
    public class VirtualTreeView : ItemsControl
    {
        private readonly TreeViewItemCollection _hierarchicalItemsSource = new TreeViewItemCollection();

        private bool _hierarchicalItemsSourceBound;

        public IList HierarchicalItems { get; } = new ObservableCollection<object>();

        public bool IsSelectionChangeActive { get; set; }

        public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
            "SelectedItem", typeof(object), typeof(VirtualTreeView), new PropertyMetadata(default(object)));

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
                    base.ItemsSource = itemsSource;
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
                    if (container != _selectedContainer)
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
                    if (container == _selectedContainer)
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

        private VirtualTreeViewItem GetGeneratedContainer(object item)
        {
            return (VirtualTreeViewItem)ItemContainerGenerator.ContainerFromItem(item);
        }

        private VirtualTreeViewItem CreateContainer(object item)
        {
            VirtualTreeViewItem treeViewItem;
            // otherwise create with two sources:
            // - template which may bind the ItemsSource property
            // - style    which may bind the IsExpanded  property
            var hierarchicalDataTemplate = ItemTemplate as HierarchicalDataTemplate;
            treeViewItem = new VirtualTreeViewItem { DataContext = item };
            if (hierarchicalDataTemplate?.ItemsSource != null)
                BindingOperations.SetBinding(treeViewItem, ItemsSourceProperty, hierarchicalDataTemplate.ItemsSource);
            // the style needs to be applied after DataContext is set, otherwise it won't bind
            treeViewItem.Style = ItemContainerStyle;
            return treeViewItem;
        }

        internal bool IsExpanded(object item)
        {
            return GetGeneratedContainer(item)?.IsExpanded ?? GetTrivialIsExpanded(item) ?? GetNonTrivialIsExpanded(item);
        }

        private string _isExpandedSourceProperty;

        private bool? GetTrivialIsExpanded(object item)
        {
            if (!OptimizeItemBindings || _isExpandedSourceProperty == null)
                return null;

            var isExpandedProperty = item.GetType().GetProperty(_isExpandedSourceProperty);
            if (isExpandedProperty == null)
                return null;

            return (bool)isExpandedProperty.GetValue(item);
        }

        private bool GetNonTrivialIsExpanded(object item)
        {
            var container = CreateContainer(item);
            if (OptimizeItemBindings)
            {
                var isExpandedBinding = BindingOperations.GetBinding(container, VirtualTreeViewItem.IsExpandedProperty);
                if (isExpandedBinding != null && isExpandedBinding.Source == null && isExpandedBinding.RelativeSource == null && isExpandedBinding.ElementName == null
                    && isExpandedBinding.Path.Path.All(IsNotSpecial))
                {
                    _isExpandedSourceProperty = isExpandedBinding.Path.Path;
                }
            }
            return container.IsExpanded;
        }

        internal IList GetChildren(object item)
        {
            return GetGeneratedContainer(item)?.ItemsSource as IList ?? GetTrivialChildren(item) ?? GetNonTrivialChildren(item);
        }

        private string _childrenSourceProperty;

        private IList GetTrivialChildren(object item)
        {
            if (!OptimizeItemBindings || _childrenSourceProperty == null)
                return null;

            var childrenSourceProperty = item.GetType().GetProperty(_childrenSourceProperty);
            if (childrenSourceProperty == null)
                return null;

            return childrenSourceProperty.GetValue(item) as IList;
        }

        private IList GetNonTrivialChildren(object item)
        {
            var container = CreateContainer(item);
            if (OptimizeItemBindings)
            {
                var childrenBinding = BindingOperations.GetBinding(container, VirtualTreeViewItem.ItemsSourceProperty);
                if (childrenBinding != null && childrenBinding.Source == null && childrenBinding.RelativeSource == null && childrenBinding.ElementName == null
                    && childrenBinding.Path.Path.All(IsNotSpecial))
                {
                    _childrenSourceProperty = childrenBinding.Path.Path;
                }
            }
            return (IList)container.ItemsSource;
        }

        private static bool IsNotSpecial(char c)
        {
            return c != '.' && c != '[';
        }
    }
}
