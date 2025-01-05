import React from 'react';

export function _reactVersion() {
    const version = React.version;
    return version;
}

export function _showModalDialog() {
    const dialog = document.getElementById("modal-add-project");
    dialog.showModal();
}

export function _closeModalDialog() {
    const dialog = document.getElementById("modal-add-project");
    dialog.close();
}
