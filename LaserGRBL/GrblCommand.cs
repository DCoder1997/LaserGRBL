﻿using System;
using System.Collections.Generic;
using System.Drawing;
using LaserGRBL.CSV;

namespace LaserGRBL
{
	public interface IGrblRow
	{
		string GetMessage();

		string GetResult(bool decode);
		string GetToolTip(bool decode);

		Color LeftColor { get; }
		Color RightColor { get; }

		int ImageIndex { get; }
	}
	
	public static class CSVD
	{
		public static CsvDictionary Settings = new CSV.CsvDictionary("LaserGRBL.CSV.setting_codes.csv", 3);
		public static CsvDictionary Alarms = new CSV.CsvDictionary("LaserGRBL.CSV.alarm_codes.csv", 2);
		public static CsvDictionary Errors = new CSV.CsvDictionary("LaserGRBL.CSV.error_codes.csv", 2);
	}

	public class GrblCommand : ICloneable, IGrblRow
	{
		public class Element
		{
			private Char mCommand;
			private Decimal mNumber;
			private string mString;

			public Element(Char Command, Decimal Number)
			{
				mCommand = Command;
				mNumber = Number;
				mString = Command + Number.ToString();
			}

			public Char Command
			{ get { return mCommand; } }

			public Decimal Number
			{ get { return mNumber; } }

		}

		private string mLine;
		private string mCodedResult;
		private TimeSpan mTimeOffset;
		private Dictionary<char, GrblCommand.Element> mHelper;

		private int mRepeatCount;

		public GrblCommand(string line)
		{ mLine = line.ToUpper().Trim(); mRepeatCount = 0; }

		public GrblCommand(string line, int repeat)
		{ mLine = line.ToUpper().Trim(); mRepeatCount = repeat; }

		public void BuildHelper()
		{
			if (mHelper != null) //just built!
				return;

			try
			{
				mHelper = new Dictionary<char, Element>();
				char cmd = '\0';
				string num = "";
				bool comment = false;
				bool oldspace = false;
				System.Text.StringBuilder sb = new System.Text.StringBuilder();

				if (!IsGrblCommand) //do not parse grbl command set
				{
					foreach (char c in mLine)
					{
						if (c == ';') //dal punto e virgola fino a fine riga -> commento!
							break;

						if (c == '(')
							comment = true;

						bool space = c == ' ' ? true : false;
						if (!comment)
						{
							if (space && !oldspace)
								sb.Append(' ');
							else if (!space)
								sb.Append(c);
						}
						oldspace = space;

						if (!comment)
						{
							if (Char.IsLetter(c))
							{
								if (cmd != '\0') //chiudi il comando precedente
									Add(new Element(cmd, Decimal.Parse(num, System.Globalization.NumberFormatInfo.InvariantInfo)));

								cmd = c; //apri il comando successivo
								num = "";
							}
							else if (Char.IsNumber(c) || c == '.' || c == '-')
							{
								num += c; //accumula il dato
							}
						}

						if (c == ')')
							comment = false;
					}

					mLine = sb.ToString();

					if (cmd != '\0')
						Add(new Element(cmd, Decimal.Parse(num, System.Globalization.NumberFormatInfo.InvariantInfo))); //aggiungi l'ultimo
				}
			}
			catch { }
		}

		public int RepeatCount
		{ get { return mRepeatCount; } }

		public void DeleteHelper()
		{mHelper = null;}
		
		public void SetOffset(decimal Distance, TimeSpan Time)
		{mTimeOffset = Time;}
		
		public TimeSpan TimeOffset
		{get {return mTimeOffset;}}
		
		public enum CommandStatus
		{ Queued, WaitingResponse, ResponseGood, ResponseBad, InvalidResponse }

		public object Clone()
		{ return MemberwiseClone(); }

		public string Command
		{ get { return mLine; } }

		private static char[] trimarray = new char[] { '\r', '\n', ' ' };

		public string SerialData
		{ 
			get
			{
				if (CanCompress)
					return mLine.Trim(trimarray).Replace(" ","") + '\n';  //strip spaces
				else
					return mLine.Trim(trimarray) + '\n';  //send it "as is"
			} 
		}

		private bool CanCompress
		{ get { return !IsGrblCommand; } }

		public string GetResult(bool decode)
		{
				if (Status == CommandStatus.ResponseBad && decode)
				{
					try
					{
						string key = mCodedResult.Substring(mCodedResult.IndexOf(':') + 1);
						string brief = CSVD.Errors.GetItem(key, 0);
						if (brief != null) return brief;
					}
					catch { }

					return mCodedResult; //if ex or null
				}

				return mCodedResult;
		}

		public CommandStatus Status
		{
			get
			{
				if (mCodedResult == null)
					return CommandStatus.Queued;
				else if (mCodedResult.Length == 0)
					return CommandStatus.WaitingResponse;
				else if (mCodedResult.StartsWith("OK"))
					return CommandStatus.ResponseGood;
				else if (mCodedResult.StartsWith("ERROR"))
					return CommandStatus.ResponseBad;
				else
					return CommandStatus.InvalidResponse;
			}
		}



		private void Add(Element element)
		{ mHelper.Add(element.Command, element); }

		public void SetResult(string result, bool decode) //ERROR:NUM
		{
			mCodedResult = result.ToUpper().Trim();
		}

		public bool IsGrblCommand
		{ get { return mLine.StartsWith("$"); } }
		
		public bool IsEmpty
		{get{return mLine.Length == 0;}}

		public bool IsWriteEEPROM
		{ get { return IsGrblCommand && IsSetConf; } } //maybe need to add G10/G28.1/G30.1 ?

		System.Text.RegularExpressions.Regex confRegEX = new System.Text.RegularExpressions.Regex(@"^[$](\d+) *= *(\d+\.?\d*)");
		private bool IsSetConf
		{ get { return confRegEX.IsMatch(mLine); } }

		
		#region G Codes

		public Element G
		{ get { return GetElement('G'); } }

		public bool IsMovement
		{ get { return IsLinearMovement || IsArcMovement; } }

		public bool IsLinearMovement
		{ get { return (X != null || Y != null) && (I == null && J == null && R == null); } }

        public bool IsRapidMovement
        {
            get
            {
                return (G != null && G.Number == 0);
            }
        }

		public bool IsArcMovement
		{ get { return I != null || J != null || R != null; } }

		public bool IsCW(bool prev)
		{
			if (G != null && G.Number == 2)
				return true;
			else if (G != null && G.Number == 3)
				return false;
			else
				return prev;
		}

		public bool IsPause
		{ get { return G != null && G.Number == 4; } }

		//G90 

		public bool IsAbsoluteCoord
		{ get { return G != null && G.Number == 90; } }

		public bool IsRelativeCoord
		{ get { return G != null && G.Number == 91; } }

		#endregion

		#region M Codes

		public Element M
		{ get { return GetElement('M'); } }

		public bool IsLaserON
		{get {return IsM3 || IsM4;}}
		
		public bool IsM3
		{ get { return M != null && M.Number == 3; } }

		public bool IsM4
		{ get { return M != null && M.Number == 4; } }
		
		public bool IsLaserOFF
		{get {return IsM5;}}
		
		public bool IsM5
		{ get { return M != null && M.Number == 5; } }

		#endregion

		#region Parameters
	
		public Element T
		{ get { return GetElement('T'); } }

		public Element S
		{ get { return GetElement('S'); } }

		public Element P
		{ get { return GetElement('P'); } }

		public Element X
		{ get { return GetElement('X'); } }

		public Element Y
		{ get { return GetElement('Y'); } }

		public Element Z
		{ get { return GetElement('Z'); } }

		public Element I
		{ get { return GetElement('I'); } }

		public Element J
		{ get { return GetElement('J'); } }

		public Element F
		{ get { return GetElement('F'); } }

		public Element R
		{ get { return GetElement('R'); } }

		#endregion

		private Element GetElement(char key)
		{ return mHelper.ContainsKey(key) ? mHelper[key] : null; }

		public double GetArcRadius()
		{
			double oX = (double)(I != null ? I.Number : 0);
			double oY = (double)(J != null ? J.Number : 0);
			return Math.Sqrt(oX * oX + oY * oY);
		}
		public PointF GetCenter(float curX, float curY)
		{
			float oX = I != null ? (float)I.Number : 0;
			float oY = J != null ? (float)J.Number : 0;

			return new PointF(curX + oX, curY + oY);
		}

		public bool TrueMovement(decimal curX, decimal curY, bool abs)
		{
			if (abs) //spostamenti assoluti
				return ((X != null && X.Number != curX) || (Y != null && Y.Number != curY));
			else //spostamenti relativi
				return ((X != null && X.Number != 0) || (Y != null && Y.Number != 0));
		}

		public string GetMessage() //per la visualizzazione
		{  return mRepeatCount == 0 ? Command : String.Format("{0} (Retry {1})", Command, mRepeatCount); } 

		public string GetToolTip(bool decode)
		{
			if (Status == CommandStatus.ResponseBad && decode)
			{
				try
				{
					string key = mCodedResult.Substring(mCodedResult.IndexOf(':') + 1);
					string tooltip = CSVD.Errors.GetItem(key, 1);
					if (tooltip != null) return tooltip;
				}
				catch { }
			}
			return "";
		}
		
		public Color LeftColor
		{ get { return ColorScheme.LogLeftCOMMAND; } }

		public Color RightColor
		{ get { return Status == CommandStatus.ResponseGood ? ColorScheme.LogRightGOOD : Status == CommandStatus.ResponseBad ? ColorScheme.LogRightBAD : ColorScheme.LogRightOTHERS; } }

		internal void SetSending()
		{mCodedResult = "";}

		public int ImageIndex
		{ get { return Status == CommandStatus.Queued || Status == CommandStatus.WaitingResponse ? 0 : Status == CommandStatus.ResponseGood ? 1 : 2; } }
	}

	public class GrblMessage : IGrblRow
	{
		public enum MessageType
		{Startup, Config, Alarm, Feedback, Position, Others}

		private string mMessage;
		private string mToolTip;
		private MessageType mType;
		
		public GrblMessage(string message, bool decode)
		{
			mMessage = message.Trim();

			if (mMessage.ToLower().StartsWith("$") || mMessage.ToLower().StartsWith("~") || mMessage.ToLower().StartsWith("!") || mMessage.ToLower().StartsWith("?") || mMessage.ToLower().StartsWith("ctrl"))
				mType = MessageType.Config;
			else if (mMessage.ToLower().StartsWith("grbl"))
				mType = MessageType.Startup;
			else if (mMessage.ToLower().StartsWith("alarm"))
				mType = MessageType.Alarm;
			else if (mMessage.StartsWith("<") && mMessage.EndsWith(">"))
				mType = MessageType.Position;
			else if (mMessage.StartsWith("[") && mMessage.EndsWith("]"))
				mType = MessageType.Feedback;
			else
				mType = MessageType.Others;
			
			if (decode)
			{
				try
				{
					if (mType == MessageType.Config) //$NUM=VAL
					{
						string key = message.Substring(1,message.IndexOf('=')-1);
						string brief = CSVD.Settings.GetItem(key, 0);
						string unit = CSVD.Settings.GetItem(key, 1);
						string desc = CSVD.Settings.GetItem(key, 2);
						
						if (brief != null)
							mMessage = string.Format("{0} ({1})", message, brief);
						
						if (desc != null)
							mToolTip = string.Format("{0} [{1}]",desc ,unit);
					}
					else if (mType == MessageType.Alarm) //ALARM:NUM
					{
						string key = message.Substring(message.IndexOf(':') + 1);
						string brief = CSVD.Alarms.GetItem(key, 0);
						string desc = CSVD.Alarms.GetItem(key, 1);
						
						mMessage = brief;
						mToolTip = desc;
					}

					
				}catch{}
			}
		}

		public string Message
		{get { return mMessage; ; }}
		
		public string GetMessage()
		{return mMessage; }

		public string GetResult(bool decode)
		{return ""; }
		
		public string GetToolTip(bool decode) //already decoded on build
		{ return mToolTip; }
		
		public Color LeftColor
		{
			get 
			{
				if (mType == MessageType.Startup)
					return ColorScheme.LogLeftSTARTUP;
				else if (mType == MessageType.Alarm)
					return ColorScheme.LogLeftALARM;
				else if (mType == MessageType.Config)
					return ColorScheme.LogLeftCONFIG;
				else if (mType == MessageType.Feedback)
					return ColorScheme.LogLeftFEEDBACK;
				else if (mType == MessageType.Position)
					return ColorScheme.LogLeftPOSITION;
				else if (mType == MessageType.Others)
					return ColorScheme.LogLeftOTHERS;
				else
					return ColorScheme.LogLeftOTHERS;
			}
		}

		public Color RightColor
		{get { return Color.Black; }} //normalmente per questi messaggi non c'è un right

		public int ImageIndex
		{ get { return -1; } }
	}
}
