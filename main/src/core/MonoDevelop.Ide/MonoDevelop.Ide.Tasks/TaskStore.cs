// 
// TaskStore.cs
//  
// Author:
//       Lluis Sanchez Gual <lluis@novell.com>
// 
// Copyright (c) 2009 Novell, Inc (http://www.novell.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:2.0.50727.3074
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using MonoDevelop.Core;
using MonoDevelop.Projects;
using MonoDevelop.Ide.Gui;
using MonoDevelop.Ide.Gui.Content;
using MonoDevelop.Ide.Navigation;
using MonoDevelop.Ide.TextEditing;
using MonoDevelop.Ide.Desktop;

namespace MonoDevelop.Ide.Tasks
{
	public class TaskStore: IEnumerable<TaskListEntry>, ILocationList
	{
		int taskUpdateCount;
		List<TaskListEntry> tasks = new List<TaskListEntry> ();
		Dictionary<FilePath,TaskListEntry[]> taskIndex = new Dictionary<FilePath, TaskListEntry[]> ();
		
		public event TaskEventHandler TasksAdded;
		public event TaskEventHandler TasksRemoved;
		public event TaskEventHandler TasksChanged;
		
		List<TaskListEntry> tasksAdded;
		List<TaskListEntry> tasksRemoved;
		
		public TaskStore ()
		{
			if (IdeApp.Workspace != null) {
				IdeApp.Workspace.FileRenamedInProject += ProjectFileRenamed;
				IdeApp.Workspace.FileRemovedFromProject += ProjectFileRemoved;
			}

			TextEditorService.LineCountChangesCommitted += delegate (object sender, TextFileEventArgs args) {
				foreach (TaskListEntry task in GetFileTasks (args.TextFile.Name.FullPath))
					task.SavedLine = -1;
			};
			
			TextEditorService.LineCountChangesReset += delegate (object sender, TextFileEventArgs args) {
				Runtime.AssertMainThread ();
				TaskListEntry[] ctasks = GetFileTasks (args.TextFile.Name.FullPath);
				foreach (TaskListEntry task in ctasks) {
					if (task.SavedLine != -1) {
						task.Line = task.SavedLine;
						task.SavedLine = -1;
					}
				}
				NotifyTasksChanged (ctasks);
			};
			
			TextEditorService.LineCountChanged += delegate (object sender, LineCountEventArgs args) {
				Runtime.AssertMainThread ();
				if (args.TextFile == null || args.TextFile.Name.IsNullOrEmpty)
					return;
				TaskListEntry[] ctasks = GetFileTasks (args.TextFile.Name.FullPath);
				foreach (TaskListEntry task in ctasks) {
					if (task.Line > args.LineNumber || (task.Line == args.LineNumber && task.Column >= args.Column)) {
						if (task.SavedLine == -1)
							task.SavedLine = task.Line;
						task.Line += args.LineCount;
					}
				}
				NotifyTasksChanged (ctasks);
			};
		}
		
		public void Add (TaskListEntry task)
		{
			Runtime.AssertMainThread ();
			tasks.Add (task);
			OnTaskAdded (task);
		}
		
		public void AddRange (IEnumerable<TaskListEntry> newTasks)
		{
			BeginTaskUpdates ();
			try {
				foreach (TaskListEntry t in newTasks) {
					tasks.Add (t);
					OnTaskAdded (t);
				}
			} finally {
				EndTaskUpdates ();
			}
		}
		
		public void RemoveRange (IEnumerable<TaskListEntry> tasks)
		{
			BeginTaskUpdates ();
			try {
				foreach (TaskListEntry t in tasks) {
					if (this.tasks.Remove (t))
						OnTaskRemoved (t);
				}
			} finally {
				EndTaskUpdates ();
			}
		}
		
		public void RemoveItemTasks (WorkspaceObject parent)
		{
			RemoveRange (new List<TaskListEntry> (GetItemTasks (parent)));
		}
		
		public void RemoveItemTasks (WorkspaceObject parent, bool checkHierarchy)
		{
			RemoveRange (new List<TaskListEntry> (GetItemTasks (parent, checkHierarchy)));
		}
		
		public void RemoveFileTasks (FilePath file)
		{
			RemoveRange (new List<TaskListEntry> (GetFileTasks (file)));
		}
		
		public void Remove (TaskListEntry task)
		{
			Runtime.AssertMainThread ();
			if (tasks.Remove (task))
				OnTaskRemoved (task);
		}
		
		public void Clear ()
		{
			try {
				BeginTaskUpdates ();
				List<TaskListEntry> toRemove = tasks;
				tasks = new List<TaskListEntry> ();
				foreach (TaskListEntry t in toRemove)
					OnTaskRemoved (t);
			} finally {
				EndTaskUpdates ();
			}
		}
		
		public void ClearByOwner (object owner)
		{
			try {
				BeginTaskUpdates ();
				List<TaskListEntry> toRemove = new List<TaskListEntry> (GetOwnerTasks (owner));
				foreach (TaskListEntry t in toRemove)
					Remove (t);
			} finally {
				EndTaskUpdates ();
			}
		}
		
		public int Count {
			get { return tasks.Count; }
		}
		
		public IEnumerator<TaskListEntry> GetEnumerator ()
		{
			return tasks.GetEnumerator ();
		}
		
		IEnumerator IEnumerable.GetEnumerator ()
		{
			return ((IEnumerable)tasks).GetEnumerator ();
		}

		public IEnumerable<TaskListEntry> GetOwnerTasks (object owner)
		{
			foreach (TaskListEntry t in tasks) {
				if (t.Owner == owner)
					yield return t;
			}
		}

		public TaskListEntry[] GetFileTasks (FilePath file)
		{
			TaskListEntry[] ta;
			if (taskIndex.TryGetValue (file, out ta))
				return ta;
			else
				return new TaskListEntry [0];
		}
		
		public IEnumerable<TaskListEntry> GetItemTasks (WorkspaceObject parent)
		{
			return GetItemTasks (parent, true);
		}
		
		public IEnumerable<TaskListEntry> GetItemTasks (WorkspaceObject parent, bool checkHierarchy)
		{
			foreach (TaskListEntry t in tasks) {
				if (t.BelongsToItem (parent, checkHierarchy))
					yield return t;
			}
		}
		
		public void BeginTaskUpdates ()
		{
			Runtime.AssertMainThread ();
			if (taskUpdateCount++ != 0)
				return;
			tasksAdded = new List<TaskListEntry> ();
			tasksRemoved = new List<TaskListEntry> ();
		}
		
		public void EndTaskUpdates ()
		{
			Runtime.AssertMainThread ();
			if (--taskUpdateCount != 0)
				return;
			List<TaskListEntry> oldAdded = tasksAdded;
			List<TaskListEntry> oldRemoved = tasksRemoved;
			tasksAdded = null;
			tasksRemoved = null;
			if (oldRemoved.Count > 0)
				NotifyTasksRemoved (oldRemoved);
			if (oldAdded.Count > 0)
				NotifyTasksAdded (oldAdded);
		}
		
		void NotifyTasksAdded (IEnumerable<TaskListEntry> ts)
		{
			try {
				if (TasksAdded != null)
					TasksAdded (null, new TaskEventArgs (ts));
			} catch (Exception ex) {
				LoggingService.LogError ("Error while notifying task changes", ex);
			}
		}
		
		void NotifyTasksChanged (IEnumerable<TaskListEntry> ts)
		{
			try {
				if (TasksChanged != null)
					TasksChanged (null, new TaskEventArgs (ts));
			} catch (Exception ex) {
				LoggingService.LogError ("Error while notifying task changes", ex);
			}
		}
		
		void NotifyTasksRemoved (IEnumerable<TaskListEntry> ts)
		{
			try {
				if (TasksRemoved != null)
					TasksRemoved (null, new TaskEventArgs (ts));
			} catch (Exception ex) {
				LoggingService.LogError ("Error while notifying task changes", ex);
			}
		}
		
		void OnTaskAdded (TaskListEntry t)
		{
			if (t.FileName != FilePath.Null) {
				TaskListEntry[] ta;
				if (taskIndex.TryGetValue (t.FileName, out ta)) {
					Array.Resize (ref ta, ta.Length + 1);
					ta [ta.Length - 1] = t;
				} else {
					ta = new TaskListEntry [] { t };
				}
				taskIndex [t.FileName] = ta;
			}
			if (tasksAdded != null)
				tasksAdded.Add (t);
			else
				NotifyTasksAdded (new TaskListEntry [] { t });
		}
		
		void OnTaskRemoved (TaskListEntry t)
		{
			if (t.FileName != FilePath.Null) {
				TaskListEntry[] ta;
				if (taskIndex.TryGetValue (t.FileName, out ta)) {
					if (ta.Length == 1) {
						if (ta [0] == t)
							taskIndex.Remove (t.FileName);
					} else {
						int i = Array.IndexOf (ta, t);
						if (i != -1) {
							TaskListEntry[] newTa = new TaskListEntry [ta.Length - 1];
							Array.Copy (ta, 0, newTa, 0, i);
							Array.Copy (ta, i+1, newTa, i, ta.Length - i - 1);
							taskIndex [t.FileName] = newTa;
						}
					}
				}
			}
			if (tasksRemoved != null)
				tasksRemoved.Add (t);
			else
				NotifyTasksRemoved (new TaskListEntry [] { t });
		}
		
		void ProjectFileRemoved (object sender, ProjectFileEventArgs args)
		{
			BeginTaskUpdates ();
			try {
				foreach (ProjectFileEventInfo e in args) {
					foreach (TaskListEntry curTask in new List<TaskListEntry> (GetFileTasks (e.ProjectFile.FilePath))) {
						Remove (curTask);
					}
				}
			} finally {
				EndTaskUpdates ();
			}
		}
		
		void ProjectFileRenamed (object sender, ProjectFileRenamedEventArgs args)
		{
			BeginTaskUpdates ();
			try {
				foreach (ProjectFileRenamedEventInfo e in args) {
					TaskListEntry[] ctasks = GetFileTasks (e.OldName);
					foreach (TaskListEntry curTask in ctasks)
						curTask.FileName = e.NewName;
					taskIndex.Remove (e.OldName);
					taskIndex [e.NewName] = ctasks;
					tasksAdded.AddRange (ctasks);
					tasksRemoved.AddRange (ctasks);
				}
			} finally {
				EndTaskUpdates ();
			}
		}
		
		#region ILocationList implementation
		
		TaskListEntry currentLocationTask;
		TaskSeverity iteratingSeverity;
		
		public void ResetLocationList ()
		{
			currentLocationTask = null;
			iteratingSeverity = TaskSeverity.Error;
		}
		
		public event EventHandler CurrentLocationTaskChanged;
		
		public TaskListEntry CurrentLocationTask {
			get { return currentLocationTask; }
			set {
				currentLocationTask = value;
				iteratingSeverity = value != null ? value.Severity : TaskSeverity.Error;
			}
		}
		
		public NavigationPoint GetNextLocation ()
		{
			return GetNextLocation (false);
		}
		
		class TaskNavigationPoint : TextFileNavigationPoint
		{
			TaskListEntry task;
			
			public TaskNavigationPoint (TaskListEntry task) : base (task.FileName, task.Line, task.Column)
			{
				this.task = task;
			}
			
			protected override Document DoShow ()
			{
				Document result = base.DoShow ();
				TaskService.InformJumpToTask (task);
				return result;
			}
		}
		
		NavigationPoint GetNextLocation (bool followSeverity)
		{
			int n;
			if (currentLocationTask == null) {
				n = 0;
				if (!followSeverity)
					iteratingSeverity = TaskSeverity.Error;
			}
			else {
				n = IndexOfTask (currentLocationTask);
				if (n != -1)
					n++;
			}
			
			// Jump over tasks with different severity or with no file name
			while (n != -1 && n < tasks.Count && 
				(iteratingSeverity != tasks [n].Severity || !IsProjectTaskFile (tasks [n])))
				n++;
			
			TaskListEntry ct = n != -1 && n < tasks.Count ? tasks [n] : null;
			if (ct == null) {
				if (iteratingSeverity != TaskSeverity.Comment) {
					iteratingSeverity++;
					currentLocationTask = null;
					return GetNextLocation (true);
				}
			}
			
			currentLocationTask = ct;
			if (CurrentLocationTaskChanged != null)
				CurrentLocationTaskChanged (this, EventArgs.Empty);
			
			if (currentLocationTask != null) {
				TaskService.ShowStatus (currentLocationTask);
				return new TaskNavigationPoint (currentLocationTask);
			}
			else {
				StatusService.MainContext.ShowMessage (GettextCatalog.GetString ("End of list"));
				return null;
			}
		}

		/// <summary>
		/// Determines whether the task's file should be opened automatically when jumping to the next error.
		/// </summary>
		public static bool IsProjectTaskFile (TaskListEntry t)
		{
			if (t.FileName.IsNullOrEmpty)
				return false;

			//only files that are part of project
			Project p = t.WorkspaceObject as Project;
			if (p == null)
				return false;
			if (p.GetProjectFile (t.FileName) == null)
				return false;

			//only text files
			var mimeType = DesktopService.GetMimeTypeForUri (t.FileName);
			if (!DesktopService.GetMimeTypeIsText (mimeType))
				return false;

			//only files for which we have a default internal display binding
			var binding = DisplayBindingService.GetDefaultBinding (t.FileName, mimeType, p);
			if (binding == null || !binding.CanUseAsDefault || binding is IExternalDisplayBinding)
				return false;

			return true;
		}
		
		
		public NavigationPoint GetPreviousLocation ()
		{
			return GetPreviousLocation (false);
		}
		
		NavigationPoint GetPreviousLocation (bool followSeverity)
		{
			int n;
			if (currentLocationTask == null) {
				n = tasks.Count - 1;
				if (!followSeverity)
					iteratingSeverity = TaskSeverity.Comment;
			}
			else {
				n = IndexOfTask (currentLocationTask);
				if (n != -1)
					n--;
			}
			
			while (n != -1 && n < tasks.Count && (iteratingSeverity != tasks [n].Severity || string.IsNullOrEmpty (tasks [n].FileName)))
				n--;
			
			TaskListEntry ct = n != -1 && n < tasks.Count ? tasks [n] : null;
			if (ct == null) {
				if (iteratingSeverity != TaskSeverity.Error) {
					iteratingSeverity--;
					currentLocationTask = null;
					return GetPreviousLocation (true);
				}
			}
			
			currentLocationTask = ct;
			if (CurrentLocationTaskChanged != null)
				CurrentLocationTaskChanged (this, EventArgs.Empty);
			
			if (currentLocationTask != null) {
				TaskService.ShowStatus (currentLocationTask);
				return new TaskNavigationPoint (currentLocationTask);
			}
			else {
				StatusService.MainContext.ShowMessage (GettextCatalog.GetString ("End of list"));
				return null;
			}
		}
		
		int IndexOfTask (TaskListEntry t)
		{
			for (int n=0; n<tasks.Count; n++) {
				if (tasks [n] == t)
					return n;
			}
			return -1;
		}
		
		public string ItemName {
			get; set;
		}
		
		#endregion
	}
		
	public delegate void TaskEventHandler (object sender, TaskEventArgs e);
	
	public class TaskEventArgs : EventArgs
	{
		IEnumerable<TaskListEntry> tasks;
		
		public TaskEventArgs (TaskListEntry task) : this (new TaskListEntry[] { task })
		{
		}
		
		public TaskEventArgs (IEnumerable<TaskListEntry> tasks)
		{
			this.tasks = tasks;
		}
		
		public IEnumerable<TaskListEntry> Tasks
		{
			get { return tasks; }
		}
	}
}
