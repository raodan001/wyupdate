﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using wyUpdate.Common;

namespace wyUpdate
{
    public partial class frmMain
    {
        delegate void ShowProgressDelegate(int weightedPercentDone, int percentDone, bool statusDone, string extraStatus, Exception ex);
        delegate void UninstallProgressDel(int percentDone, int stepOn, string extraStatus, Exception ex);
        delegate void CheckProcessesDel(List<FileInfo> files, bool statusDone);

        delegate void ChangeRollbackDelegate(bool rbRegistry);


        // update the label & progress bar when downloading/updating
        void ShowProgress(int percentDone, int unweightedPercent, bool done, string extraStatus, Exception ex)
        {
            //update progress bar when between 0 and 100
            if (percentDone > -1 && percentDone < 101)
            {
                panelDisplaying.Progress = percentDone;

                // send the progress to the AutoUpdate control
                if (isAutoUpdateMode && autoUpdateStepProcessing != UpdateStep.Install)
                    updateHelper.SendProgress(unweightedPercent, autoUpdateStepProcessing);
            }

            //update bottom status
            if (extraStatus != panelDisplaying.ProgressStatus && extraStatus != "")
                panelDisplaying.ProgressStatus = extraStatus;

            if (done && ex == null)
            {
                if (isCancelled)
                    Close(); //close the form
                else if (frameOn == Frame.Checking)
                {
                    //set the serverfile location
                    serverFileLoc = downloader.DownloadingTo;
                    try
                    {
                        ServerDownloadedSuccessfully();
                    }
                    catch (NoUpdatePathToNewestException)
                    {
                        //there is no update path to the newest version
                        ShowFrame(Frame.NoUpdatePathAvailable);
                        return;
                    }
                    catch (Exception e)
                    {
                        ex = e; //error occured, show error screen
                    }
                }
                else
                {
                    if (update.CurrentlyUpdating == UpdateOn.DownloadingUpdate)
                        updateFilename = downloader.DownloadingTo;

                    // continue on to next step
                    StepCompleted();
                }
            }


            if (ex != null)
            {
                //Show the error (rollback has already been done)
                if (frameOn == Frame.Checking)
                {
                    error = clientLang.ServerError;
                    errorDetails = ex.Message;
                }
                else
                {
                    if (update.CurrentlyUpdating == UpdateOn.DownloadingUpdate)
                    {
                        //a download error occurred
                        error = clientLang.DownloadError;
                        errorDetails = ex.Message;
                    }
                    else // an update error occurred
                    {
                        // if the exception was PatchApplicationException, then
                        //see if a catch-all update exists (and the catch-all update isn't the one that failed)
                        if (ex.GetType() == typeof(PatchApplicationException) &&
                            updateFrom != update.VersionChoices[update.VersionChoices.Count - 1] &&
                            update.VersionChoices[update.VersionChoices.Count - 1].Version == update.NewVersion)
                        {
                            updateFrom = update.VersionChoices[update.VersionChoices.Count - 1];

                            error = null;

                            panelDisplaying.UpdateItems[1].Status = UpdateItemStatus.Nothing;

                            // we're no longer extracting
                            if (isAutoUpdateMode)
                                autoUpdateStepProcessing = UpdateStep.DownloadUpdate;

                            // download the catch-all update
                            DownloadUpdate();

                            // set for auto-updates
                            currentlyExtracting = false;

                            return;
                        }

                        error = clientLang.GeneralUpdateError;
                        errorDetails = ex.Message;
                    }
                }

                if (isAutoUpdateMode)
                    updateHelper.SendFailed(error, errorDetails, autoUpdateStepProcessing);

                ShowFrame(Frame.Error);
            }
        }


        void SelfUpdateProgress(int percentDone, int unweightedProgress, bool done, string extraStatus, Exception ex)
        {
            //update progress bar
            panelDisplaying.Progress = percentDone;

            //update bottom status
            if (extraStatus != panelDisplaying.ProgressStatus && extraStatus != "")
                panelDisplaying.ProgressStatus = extraStatus;

            if (done && ex == null)
            {
                if (isCancelled)
                    Close(); //close the form
                else if (frameOn == Frame.Checking)
                {
                    clientSFLoc = downloader.DownloadingTo;

                    try
                    {
                        // client server file downloaded sucessfully
                        DownloadClientSFSuccess();
                    }
                    catch (Exception e)
                    {
                        ex = e;
                    }
                }
                else
                {
                    switch (update.CurrentlyUpdating)
                    {
                        case UpdateOn.DownloadingSelfUpdate:

                            panelDisplaying.UpdateItems[0].Status = UpdateItemStatus.Success;

                            //set the filename of the downloaded client update file
                            updateFilename = downloader.DownloadingTo;

                            //begin extracting and installing the update
                            update.CurrentlyUpdating = UpdateOn.FullSelfUpdate;
                            InstallUpdates(update.CurrentlyUpdating);
                            break;

                        case UpdateOn.FullSelfUpdate:

                            panelDisplaying.UpdateItems[1].Status = UpdateItemStatus.Success;

                            //start the newly installed client and resume "normal" downloading & updating
                            StartSelfElevated();
                            break;

                        case UpdateOn.ExtractSelfUpdate:

                            //TODO: save autoupdate state (selfupdate downloaded)
                            //TODO: start new wyUpdate in "master mode" (switch this to "slave" mode)
                            //TODO: Tell master to begin downloading the update

                            break;

                        case UpdateOn.InstallSelfUpdate:

                            //TODO: install self update should be called first
                            //TODO: then this deletes self-update details & self backup (no longer need self update even if main update fails)
                            //TODO: save autoupdate file (no longer need self update)
                            //TODO: begin main update install

                            break;
                    }
                }
            }


            if (ex != null)
            {
                bool selfUpdateRequired =
                    VersionTools.Compare(VersionTools.FromExecutingAssembly(), update.MinClientVersion) == -1;

                bool canTryCatchAllUpdate = frameOn != Frame.Checking

                                            // patch failed
                                            && ex.GetType() == typeof (PatchApplicationException)

                                            // if the catch-all update isn't the one that failed
                                            && updateFrom != update.VersionChoices[update.VersionChoices.Count - 1]

                                            // and there is a catch-all update
                                            && update.VersionChoices[update.VersionChoices.Count - 1].Version == update.NewVersion;
                

                // if a new client is *required* to install the update...
                if (selfUpdateRequired && !canTryCatchAllUpdate)
                {
                    //show an error and bail out
                    error = clientLang.SelfUpdateInstallError;
                    errorDetails = ex.Message;

                    ShowFrame(Frame.Error);
                }
                else if (frameOn == Frame.Checking)
                {
                    //client server file failed to download, continue as usual:
                    SelfUpdateState = SelfUpdateState.None;

                    //Show update info page
                    ShowFrame(Frame.UpdateInfo);
                }
                else
                {
                    if (canTryCatchAllUpdate)
                    {
                        // select the catch all update
                        updateFrom = update.VersionChoices[update.VersionChoices.Count - 1];

                        // clear errors
                        error = null;
                        errorDetails = null;

                        panelDisplaying.UpdateItems[1].Status = UpdateItemStatus.Nothing;

                        // download the catch-all update
                        DownloadUpdate();

                        return;
                    }


                    //self-update failed to download or install
                    //just relaunch old client and continue with update
                    StartSelfElevated();
                }
            }
        }

        void UninstallProgress(int percentDone, int step, string extraStatus, Exception ex)
        {
            //update progress bar
            panelDisplaying.Progress = percentDone;

            //update bottom status
            if (extraStatus != panelDisplaying.ProgressStatus && extraStatus != "")
                panelDisplaying.ProgressStatus = extraStatus;

            //step: 0=working, 1=uninstalling registry, 2=done
            if (step == 1)
            {
                panelDisplaying.UpdateItems[0].Status = UpdateItemStatus.Success;
                SetStepStatus(1, clientLang.UninstallRegistry);
            }
            else if (step == 2 && ex == null)
            {
                //just bail out.
                Close();
            }
            else if (ex != null)
            {
                if (isSilent)
                    Close();
                else
                {
                    //Show the error (rollback has ocurred)
                    error = ex.Message;
                    ShowFrame(Frame.Error);
                }
            }
        }

        void CheckProcess(List<FileInfo> files, bool done)
        {
            if (done)
            {
                if (files != null)//if there are some files needing closing
                {
                    // show myself, make topmost
                    Show();
                    TopMost = true;
                    TopMost = false;

                    // start the close processes form
                    Form proc = new frmProcesses(files, clientLang);
                    DialogResult result = proc.ShowDialog();

                    if (result == DialogResult.Cancel)
                    {
                        //cancel the update process
                        CancelUpdate(true);
                    }
                    else
                    {
                        //processes closed, continue on
                        update.CurrentlyUpdating += 1;
                        InstallUpdates(update.CurrentlyUpdating);
                    }
                }
                else
                {
                    //no processes need to be closed, continue on
                    update.CurrentlyUpdating += 1;
                    InstallUpdates(update.CurrentlyUpdating);
                }
            }
        }

        void ChangeRollback(bool rbRegistry)
        {
            DisableCancel();

            // set the error icon to current progress item
            if (rbRegistry)
            {
                // error updating the registry
                panelDisplaying.UpdateItems[2].Status = UpdateItemStatus.Error;

                SetStepStatus(3, clientLang.RollingBackRegistry);
            }
            else if (panelDisplaying.UpdateItems[2].Status != UpdateItemStatus.Error)
            {
                // error updating the files
                panelDisplaying.UpdateItems[1].Status = UpdateItemStatus.Error;

                SetStepStatus(2, clientLang.RollingBackFiles);
            }
            else
            {
                SetStepStatus(3, clientLang.RollingBackFiles);
            }
        }
    }
}