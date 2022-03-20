using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace CoT
{
  [Transaction( TransactionMode.Manual )]
  [Regeneration( RegenerationOption.Manual )]
  [Journaling( JournalingMode.NoCommandData )]
  public class Application : IExternalApplication
  {
    public string Location;

    public Result OnStartup( UIControlledApplication application )
    {
      Location = Assembly.GetAssembly( GetType( ) ).Location;

      var ribbon = "SUTD-URA";
      application.CreateRibbonTab( ribbon );

      var panel = application.CreateRibbonPanel( ribbon, "IO" );

      AddButton( panel, "Json", "Revit to Json Export Addin", "CoT." + nameof( RevitToJson ), "sutd.png" );

      return Result.Succeeded;
    }

    public RibbonItem AddButton( RibbonPanel panel, string caption,
        string tooltip, string command, string image )
    {      
      var logo = Imaging.CreateBitmapSourceFromHBitmap( 
        Properties.Resources.Sutd.GetHbitmap( ), 
        IntPtr.Zero, Int32Rect.Empty, 
        BitmapSizeOptions.FromEmptyOptions( ) );
      
      return panel.AddItem( new PushButtonData(
          $"Button{Guid.NewGuid( )}", caption, Location, command )
      {
          ToolTip = tooltip, LargeImage = logo
      } );
    }

    public Result OnShutdown( UIControlledApplication application ) => Result.Succeeded;
  }
}
