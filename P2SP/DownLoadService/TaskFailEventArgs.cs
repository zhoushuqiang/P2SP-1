﻿/*
 * Created by SharpDevelop.
 * User: Pengzhihu
 * Date: 2013-7-4
 * Time: 11:15
 * 
 * To change this template use Tools | Options | Coding | Edit Standard Headers.
 */
using System;

namespace DownLoadService
{
	/// <summary>
	/// Description of TaskFailEventArgs.
	/// </summary>
	
	public delegate void TaskFailEventHandler(object sender, TaskFailEventArgs e);
	
	public class TaskFailEventArgs : EventArgs
	{
		public TaskFailEventArgs()
		{
		}
	}
}
