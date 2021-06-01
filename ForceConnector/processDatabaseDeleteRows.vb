﻿Imports System.ComponentModel
Imports System.Windows.Forms

Public Class processDatabaseDeleteRows

    Dim statusText As String = ""

    Dim excelApp As Excel.Application
    Dim workbook As Excel.Workbook
    Dim worksheet As Excel.Worksheet

    Dim g_objectType As String                  ' current entity table, ie "Account"
    Dim g_sfd As RESTful.DescribeSObjectResult  ' global describe for current table, not really needed since toolkit caches
    Dim g_table As Excel.Range                  ' all current region, with table name and header row
    Dim g_header As Excel.Range                 ' row with column labels
    Dim g_body As Excel.Range                   ' area with data, just below the header row
    Dim g_ids As Excel.Range                    ' column with salesforce ID's
    Dim g_start As Excel.Range                  ' some globals to hold info about the region we are working on

    Private Sub processDatabaseDeleteRows_Load(sender As Object, e As EventArgs) Handles Me.Load
        Try
            lblMessage.Font = New Drawing.Font(lblMessage.Font, Drawing.FontStyle.Regular)
            lblMessage.ForeColor = Drawing.Color.Black
            statusText = ""

            '' these properties should be set to True (at design-time or runtime) before calling the RunWorkerAsync
            '' to ensure that it supports Cancellation and reporting Progress
            bgw.WorkerSupportsCancellation = True
            bgw.WorkerReportsProgress = True

            '' call this method to start your asynchronous Task.
            bgw.RunWorkerAsync()

            GoTo done
        Catch ex As Exception
            statusText = ex.Message
        End Try

errors:
        If statusText <> "" Then
            progressDownload.Value = 100
            lblMessage.Font = New Drawing.Font(lblMessage.Font, Drawing.FontStyle.Bold)
            lblMessage.ForeColor = Drawing.Color.Red
            lblMessage.Text = statusText
            btnAction.Text = "Done"
            btnAction.Enabled = True
        End If
done:
    End Sub

    Private Sub btnAction_Click(sender As Object, e As EventArgs) Handles btnAction.Click
        '' to cancel the task, just call the BackgroundWorker.CancelAsync method.
        If btnAction.Text = "Cancel" Then
            bgw.CancelAsync()
        ElseIf btnAction.Text = "Done" Then
            MyBase.Close()
        End If
    End Sub

    Private Sub bgw_DoWork(sender As Object, e As DoWorkEventArgs) Handles bgw.DoWork
        '' The asynchronous task you want to perform goes here
        '' the following is an example of how it typically goes.
        Dim totals As Long = 0L
        Dim outrow As Long = 0L
        Try
            excelApp = ThisAddIn.excelApp
            workbook = excelApp.ActiveWorkbook
            worksheet = workbook.ActiveSheet

            excelApp.StatusBar = "Query Database Table..."
            setControlText(btnAction, "Wait...")
            bgw.ReportProgress(0, "Please wait for initialize...")

            If Not checkSession() Then
                statusText = "Session Failed!"
                GoTo errors
            End If
            If Not setDataRanges(excelApp, worksheet, g_table, g_start, g_header, g_body, g_objectType, g_ids, g_sfd, statusText) Then
                GoTo errors
            End If

            bgw.ReportProgress(0, "Build Query String...")

            totals = excelApp.Selection.Rows.Count

            If Not RegQueryBoolValue(NOLIMITS) And totals > maxRows Then
                statusText = "too many rows selected " & totals.ToString("N0") & ", max is " & maxRows.ToString("N0")
                GoTo errors
            End If

            setControlText(btnAction, "Cancel")
            setControlStatus(btnAction, True)

            Dim msg As String = "You try to download " & totals.ToString("N0") & " records. Are you sure?"
            Dim result As DialogResult = TopMostMessageBox.Show("Query Information", msg, MessageBoxButtons.OKCancel, MessageBoxIcon.Question)
            If result = DialogResult.Cancel Then
                statusText = "Cancel Query"
                GoTo cancel
            End If

            Dim row_pointer As Integer = excelApp.Selection.row ' row where we start to chunk
            Dim chunk As Excel.Range
            Do ' build a chunk Range which covers the cells we can update in a batch
                chunk = excelApp.Intersect(excelApp.Selection, excelApp.ActiveSheet.Rows(row_pointer))
                If chunk Is Nothing Then Exit Do

                chunk = chunk.Resize(maxBatchSize) ' extend the chunk to cover our batchsize
                chunk = excelApp.Intersect(excelApp.Selection, chunk) ' trim the last chunk !
                row_pointer = row_pointer + maxBatchSize ' up our pos counter

                Dim percent As Integer = CInt((outrow / totals) * 100)
                If percent > 100 Then percent = 100
                bgw.ReportProgress(percent, "Delete " & chunk.Count.ToString() & " records from row " & outrow.ToString("N0"))

                chunk.Interior.ColorIndex = 36 ' show off...
                If Not deleteSelectedRange(excelApp, worksheet, g_ids, g_objectType, chunk) Then GoTo done ' do it
                chunk.Interior.ColorIndex = 0

                '' check at regular intervals for CancellationPending
                If bgw.CancellationPending Then
                    'bgw.ReportProgress(percent, "Cancelling...")
                    bgw.ReportProgress(CInt((outrow / totals) * 100), "Cancelling...")
                    Exit Do
                End If

                outrow = outrow + chunk.Count
            Loop Until chunk Is Nothing

            '' any cleanup code go here
            '' ensure that you close all open resources before exitting out of this Method.
            '' try to skip off whatever is not desperately necessary if CancellationPending is True
            bgw.ReportProgress(100, "Complete.")
            '' set the e.Cancel to True to indicate to the RunWorkerCompleted that you cancelled out
            If bgw.CancellationPending Then
                e.Cancel = True
                bgw.ReportProgress(100, "Cancelled.")
            End If

            GoTo done
        Catch ex As Exception
            statusText = ex.Message
            GoTo errors
        End Try

cancel:
        statusText = "Delete Canceled!"
        e.Cancel = True
        GoTo done
errors:
        If statusText <> "" Then
            ErrorBox(statusText)
            statusText = "DeleteData Error!"
            e.Cancel = True
        End If
done:
        excelApp.StatusBar = "Delete complete, " & outrow - 1 & " total rows deleted"
        excelApp.ScreenUpdating = True

    End Sub

    Private Sub bgw_ProgressChanged(sender As Object, e As ProgressChangedEventArgs) Handles bgw.ProgressChanged
        '' This event is fired when you call the ReportProgress method from inside your DoWork.
        '' Any visual indicators about the progress should go here.
        progressDownload.Value = CInt(e.ProgressPercentage)
        lblMessage.Text = e.UserState
    End Sub

    Private Sub bgw_RunWorkerCompleted(sender As Object, e As RunWorkerCompletedEventArgs) Handles bgw.RunWorkerCompleted
        '' This event is fired when your BackgroundWorker exits.
        '' It may have exitted Normally after completing its task, 
        '' or because of Cancellation, or due to any Error.

        If e.Error IsNot Nothing Then
            '' if BackgroundWorker terminated due to error
            'MessageBox.Show(e.Error.Message)
            lblMessage.Font = New Drawing.Font(lblMessage.Font, Drawing.FontStyle.Bold)
            lblMessage.ForeColor = Drawing.Color.Red
            lblMessage.Text = "Error occurred!" & e.Error.Message

        ElseIf e.Cancelled Then
            '' otherwise if it was cancelled
            'MessageBox.Show("Download cancelled!")
            If statusText <> "" Then
                lblMessage.Font = New Drawing.Font(lblMessage.Font, Drawing.FontStyle.Bold)
                lblMessage.ForeColor = Drawing.Color.Red
                lblMessage.Text = statusText
            Else
                lblMessage.Text = "Delete Cancelled!"
            End If

        Else
            '' otherwise it completed normally
            'MessageBox.Show("Download completed!")
            lblMessage.Text = "Delete completed!"
        End If

        btnAction.Text = "Done"
        btnAction.Enabled = True
    End Sub

    '******************************************************
    '* Change controls label in the bgw.DoWork()
    '* ctl -> label, button ...etc
    '******************************************************
    Private Sub setControlText(ByVal ctl As Control, ByVal text As String)
        If ctl.InvokeRequired Then
            ctl.Invoke(New setControlTextInvoker(AddressOf setControlText), ctl, text)
        Else
            ctl.Text = text
        End If
    End Sub
    Private Delegate Sub setControlTextInvoker(ByVal ctl As Control, ByVal text As String)

    Private Sub setControlStatus(ByVal ctl As Control, ByVal enabled As Boolean)
        If ctl.InvokeRequired Then
            ctl.Invoke(New setControlStatusInvoker(AddressOf setControlStatus), ctl, enabled)
        Else
            ctl.Enabled = enabled
        End If
    End Sub
    Private Delegate Sub setControlStatusInvoker(ByVal ctl As Control, ByVal enabled As Boolean)
End Class