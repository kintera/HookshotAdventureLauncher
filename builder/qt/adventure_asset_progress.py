"""Qt adapter: 'Creating Save Assets' popup, only while cache needs building.

Use AFTER the main Qt QApplication exists. Worker does not create QPixmap or
modify UI; all BNTX work happens off the GUI thread. Returns False on failure
so the existing generated art continues to render unchanged.
"""
from __future__ import annotations


def ensure_save_assets(parent, cache, *, notify_failure=True, force=False):
    """Return True only when the complete local cache is usable.

    Integration hook for Hookshot's launcher startup, not called by the editor.
    """
    from PyQt6.QtCore import QObject, QThread, Qt, QTimer, pyqtSignal, pyqtSlot
    from PyQt6.QtWidgets import QDialog, QLabel, QProgressBar, QVBoxLayout, QMessageBox

    if cache.current().ready and not force:
        return True  # never flash a progress dialog for already-generated assets

    class Worker(QObject):
        progress = pyqtSignal(int, int, str)
        complete = pyqtSignal(object)

        @pyqtSlot()
        def work(self):
            result = cache.generate(lambda i, n, r: self.progress.emit(i, n, r), force=force)
            self.complete.emit(result)

    class GenerationDialog(QDialog):
        """Keep the modal progress UI alive while its worker writes the cache."""

        def __init__(self, parent=None):
            super().__init__(parent)
            self.finished_generating = False

        def reject(self):
            # QDialog's Escape shortcut calls reject(); early shutdown could
            # destroy the QThread while icon decoding is still underway.
            if self.finished_generating:
                super().reject()

        def closeEvent(self, event):
            if not self.finished_generating:
                event.ignore()
                return
            super().closeEvent(event)

    dialog = GenerationDialog(parent)
    dialog.setWindowTitle('Creating Save Assets')
    dialog.setWindowModality(Qt.WindowModality.ApplicationModal)
    dialog.setMinimumWidth(430)
    layout = QVBoxLayout(dialog)
    title = QLabel('Creating Save Assets')
    title.setStyleSheet('font-size: 15px; font-weight: 700;')
    subtitle = QLabel('Decoding your locally installed game artwork…')
    subtitle.setWordWrap(True)
    progress = QProgressBar()
    progress.setRange(0, 0)
    current_name = QLabel('Preparing textures…')
    layout.addWidget(title)
    layout.addWidget(subtitle)
    layout.addWidget(progress)
    layout.addWidget(current_name)
    # No cancel button: staging is atomic, and the UI opens after completion.
    thread = QThread(dialog)
    worker = Worker()
    worker.moveToThread(thread)
    status = {'result': None}

    def on_progress(i, total, name):
        progress.setRange(0, max(total, 1))
        progress.setValue(i)
        current_name.setText(f'{name} ({i}/{total})')

    def on_complete(result):
        status['result'] = result
        thread.quit()
        thread.wait()  # worker has finished decoding before we close the dialog
        dialog.finished_generating = True
        dialog.accept()

    thread.started.connect(worker.work)
    worker.progress.connect(on_progress)
    worker.complete.connect(on_complete)
    # Keep the worker alive until after signal delivery, avoid QThread destroyed running.
    QTimer.singleShot(0, thread.start)
    dialog.exec()
    if thread.isRunning():
        thread.quit()
        thread.wait()
    result = status['result']
    if result and result.ready:
        return True
    if notify_failure:
        detail = result.error if result else 'The background generation task did not complete.'
        QMessageBox.warning(parent, 'Save Assets Unavailable',
                            'Could not create save assets. Existing generated icons will be used.\n\n' + detail)
    return False
