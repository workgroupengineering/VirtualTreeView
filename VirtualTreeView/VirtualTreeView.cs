﻿// VirtualTreeView - a TreeView that *actually* allows virtualization
// https://github.com/picrap/VirtualTreeView

namespace VirtualTreeView
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Collections.Specialized;
    using System.ComponentModel;
    using System.Windows;
    using System.Windows.Automation.Peers;
    using System.Windows.Controls;
    using System.Windows.Data;
    using System.Windows.Markup;
    using MS.Internal;
    using MS.Utility;
    using Reflection;

    [StyleTypedProperty(Property = nameof(ItemContainerStyle), StyleTargetType = typeof(TreeViewItem))]
    [ContentProperty(nameof(HierarchicalItems))]
    public class VirtualTreeView : ItemsControl
    {
        public static readonly DependencyProperty HierarchicalItemsSourceProperty = DependencyProperty.Register(
            nameof(HierarchicalItemsSource), typeof(TreeViewItemCollection), typeof(VirtualTreeView), new PropertyMetadata(null, OnHierarchicalItemsSourceChanged));

        public TreeViewItemCollection HierarchicalItemsSource
        {
            get { return (TreeViewItemCollection)GetValue(HierarchicalItemsSourceProperty); }
            set { SetValue(HierarchicalItemsSourceProperty, value); }
        }

        public IList HierarchicalItems { get; } = new ObservableCollection<object>();

        public bool IsSelectionChangeActive { get; set; }

        public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
            "SelectedItem", typeof(object), typeof(VirtualTreeView), new PropertyMetadata(default(object)));

        public object SelectedItem
        {
            get { return (object)GetValue(SelectedItemProperty); }
            set { SetValue(SelectedItemProperty, value); }
        }

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
        }

        public VirtualTreeView()
        {
            HierarchicalItems.IfType<INotifyCollectionChanged>(nc => nc.CollectionChanged += OnHierarchicalItemsCollectionChanged);
        }

        private static void OnHierarchicalItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var @this = (VirtualTreeView)d;
            e.OldValue.IfType<INotifyCollectionChanged>(nc => nc.CollectionChanged -= @this.OnHierarchicalItemsSourceCollectionChanged);
            e.NewValue.IfType<INotifyCollectionChanged>(nc => nc.CollectionChanged += @this.OnHierarchicalItemsSourceCollectionChanged);
        }

        private void OnHierarchicalItemsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (HierarchicalItemsSource != null)
                throw new InvalidOperationException("HierarchicalItemsSource is data bound, do no use HierarchicalItems");
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    AppendRange(e.NewItems);
                    break;
                case NotifyCollectionChangedAction.Remove:
                    break;
                case NotifyCollectionChangedAction.Replace:
                    break;
                case NotifyCollectionChangedAction.Move:
                    break;
                case NotifyCollectionChangedAction.Reset:
                    Items.Clear();
                    AppendRange(HierarchicalItems);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void OnHierarchicalItemsSourceCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    break;
                case NotifyCollectionChangedAction.Remove:
                    break;
                case NotifyCollectionChangedAction.Replace:
                    break;
                case NotifyCollectionChangedAction.Move:
                    break;
                case NotifyCollectionChangedAction.Reset:
                    Items.Clear();
                    foreach (var i in HierarchicalItemsSource)
                        Append(i);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void Append(object o) => Add(Items.Count, o);
        private void AppendRange(IList l) => AddRange(Items.Count, l);

        private void Add(int index, object o)
        {
            Items.Insert(index, o);
        }

        private void AddRange(int index, IList l)
        {
            foreach (var i in l)
                Items.Insert(index++, i);
        }
        internal void HandleSelectionAndCollapsed(VirtualTreeViewItem collapsed)
        {
            //if ((_selectedContainer != null) && (_selectedContainer != collapsed))
            //{
            //    // Check if current selection is under the collapsed element
            //    TreeViewItem current = _selectedContainer;
            //    do
            //    {
            //        current = current.ParentTreeViewItem;
            //        if (current == collapsed)
            //        {
            //            TreeViewItem oldContainer = _selectedContainer;

            //            ChangeSelection(collapsed.ParentItemsControl.ItemContainerGenerator.ItemFromContainer(collapsed), collapsed, true);

            //            if (oldContainer.IsKeyboardFocusWithin)
            //            {
            //                // If the oldContainer had focus then move focus to the newContainer instead
            //                _selectedContainer.Focus();
            //            }

            //            break;
            //        }
            //    }
            //    while (current != null);
            //}
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
    }
}
