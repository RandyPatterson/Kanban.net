// Kanban Board JavaScript
let allCards = [];
let allLabels = [];
let draggedCard = null;

const API = {
    cards: '/api/cards',
    labels: '/api/labels'
};

// ── Init ──
document.addEventListener('DOMContentLoaded', async () => {
    await loadLabels();
    await loadCards();
    setupDragAndDrop();
    setupSearch();
});

// ── Data Loading ──
async function loadCards() {
    const res = await fetch(API.cards);
    allCards = await res.json();
    renderCards();
}

async function loadLabels() {
    const res = await fetch(API.labels);
    allLabels = await res.json();
    renderLabelFilter();
}

// ── Rendering ──
function renderCards() {
    const columns = ['todo', 'inprogress', 'done'];
    const searchTerm = document.getElementById('searchInput').value.toLowerCase();
    const labelFilter = document.getElementById('labelFilter').value;

    columns.forEach(col => {
        const list = document.getElementById(`list-${col}`);
        const cards = allCards
            .filter(c => c.column === col)
            .sort((a, b) => a.position - b.position);

        list.innerHTML = '';
        let visibleCount = 0;

        cards.forEach(card => {
            const matchesSearch = !searchTerm ||
                card.title.toLowerCase().includes(searchTerm) ||
                card.description.toLowerCase().includes(searchTerm);
            const matchesLabel = !labelFilter ||
                card.labelIds.includes(labelFilter);
            const visible = matchesSearch && matchesLabel;
            if (visible) visibleCount++;

            const el = document.createElement('div');
            el.className = `kanban-card${visible ? '' : ' hidden'}`;
            el.draggable = true;
            el.dataset.id = card.id;

            const labelsHtml = card.labelIds
                .map(lid => {
                    const label = allLabels.find(l => l.id === lid);
                    return label
                        ? `<span class="label-chip" style="background:${label.color}">${escapeHtml(label.name)}</span>`
                        : '';
                })
                .join('');

            el.innerHTML = `
                ${labelsHtml ? `<div class="card-labels">${labelsHtml}</div>` : ''}
                <div class="card-title">${escapeHtml(card.title)}</div>
                ${card.description ? `<div class="card-description">${renderMarkdown(card.description)}</div>` : ''}
                <div class="card-actions">
                    <button onclick="editCard('${card.id}')" title="Edit">✏️ Edit</button>
                    <button onclick="deleteCardDirect('${card.id}')" title="Delete">🗑️</button>
                </div>
            `;

            // Drag events on card
            el.addEventListener('dragstart', handleDragStart);
            el.addEventListener('dragend', handleDragEnd);

            list.appendChild(el);
        });

        document.getElementById(`count-${col}`).textContent = visibleCount;
    });
}

function renderLabelFilter() {
    const select = document.getElementById('labelFilter');
    const current = select.value;
    select.innerHTML = '<option value="">All Labels</option>';
    allLabels.forEach(l => {
        const opt = document.createElement('option');
        opt.value = l.id;
        opt.textContent = l.name;
        opt.style.color = l.color;
        select.appendChild(opt);
    });
    select.value = current;
}

// ── Search & Filter ──
function setupSearch() {
    document.getElementById('searchInput').addEventListener('input', renderCards);
    document.getElementById('labelFilter').addEventListener('change', renderCards);
}

// ── Card CRUD ──
function openCardModal(column) {
    document.getElementById('cardModalTitle').textContent = 'Add Card';
    document.getElementById('cardId').value = '';
    document.getElementById('cardColumn').value = column;
    document.getElementById('cardTitle').value = '';
    document.getElementById('cardDescription').value = '';
    document.getElementById('btnDeleteCard').style.display = 'none';
    renderCardLabelCheckboxes([]);
    new bootstrap.Modal(document.getElementById('cardModal')).show();
}

function editCard(id) {
    const card = allCards.find(c => c.id === id);
    if (!card) return;
    document.getElementById('cardModalTitle').textContent = 'Edit Card';
    document.getElementById('cardId').value = card.id;
    document.getElementById('cardColumn').value = card.column;
    document.getElementById('cardTitle').value = card.title;
    document.getElementById('cardDescription').value = card.description;
    document.getElementById('btnDeleteCard').style.display = 'inline-block';
    renderCardLabelCheckboxes(card.labelIds);
    new bootstrap.Modal(document.getElementById('cardModal')).show();
}

function renderCardLabelCheckboxes(selectedIds) {
    const container = document.getElementById('cardLabels');
    container.innerHTML = '';
    if (allLabels.length === 0) {
        container.innerHTML = '<small class="text-muted">No labels yet. Create labels first.</small>';
        return;
    }
    allLabels.forEach(label => {
        const isSelected = selectedIds.includes(label.id);
        const div = document.createElement('div');
        div.className = `label-checkbox${isSelected ? ' selected' : ''}`;
        div.style.backgroundColor = label.color;
        div.innerHTML = `
            <input type="checkbox" value="${label.id}" ${isSelected ? 'checked' : ''} />
            ${escapeHtml(label.name)}
        `;
        div.addEventListener('click', () => {
            const cb = div.querySelector('input');
            cb.checked = !cb.checked;
            div.classList.toggle('selected', cb.checked);
        });
        container.appendChild(div);
    });
}

async function saveCard() {
    const id = document.getElementById('cardId').value;
    const title = document.getElementById('cardTitle').value.trim();
    if (!title) { alert('Title is required'); return; }

    const labelIds = Array.from(document.querySelectorAll('#cardLabels input:checked'))
        .map(cb => cb.value);

    const body = {
        title,
        description: document.getElementById('cardDescription').value.trim(),
        column: document.getElementById('cardColumn').value,
        labelIds
    };

    if (id) {
        await fetch(`${API.cards}/${id}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
    } else {
        await fetch(API.cards, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
    }

    bootstrap.Modal.getInstance(document.getElementById('cardModal')).hide();
    await loadCards();
}

async function deleteCard() {
    const id = document.getElementById('cardId').value;
    if (!id || !confirm('Delete this card?')) return;
    await fetch(`${API.cards}/${id}`, { method: 'DELETE' });
    bootstrap.Modal.getInstance(document.getElementById('cardModal')).hide();
    await loadCards();
}

async function deleteCardDirect(id) {
    if (!confirm('Delete this card?')) return;
    await fetch(`${API.cards}/${id}`, { method: 'DELETE' });
    await loadCards();
}

// ── Drag & Drop ──
function setupDragAndDrop() {
    document.querySelectorAll('.card-list').forEach(list => {
        list.addEventListener('dragover', handleDragOver);
        list.addEventListener('dragenter', handleDragEnter);
        list.addEventListener('dragleave', handleDragLeave);
        list.addEventListener('drop', handleDrop);
    });
}

function handleDragStart(e) {
    draggedCard = e.target.closest('.kanban-card');
    draggedCard.classList.add('dragging');
    e.dataTransfer.effectAllowed = 'move';
    e.dataTransfer.setData('text/plain', draggedCard.dataset.id);
}

function handleDragEnd() {
    if (draggedCard) {
        draggedCard.classList.remove('dragging');
        draggedCard = null;
    }
    document.querySelectorAll('.drag-over').forEach(el => el.classList.remove('drag-over'));
    document.querySelectorAll('.drop-placeholder').forEach(el => el.remove());
}

function handleDragEnter(e) {
    e.preventDefault();
    const list = e.target.closest('.card-list');
    if (list) list.classList.add('drag-over');
}

function handleDragLeave(e) {
    const list = e.target.closest('.card-list');
    if (list && !list.contains(e.relatedTarget)) {
        list.classList.remove('drag-over');
    }
}

function handleDragOver(e) {
    e.preventDefault();
    e.dataTransfer.dropEffect = 'move';

    const list = e.target.closest('.card-list');
    if (!list) return;

    // Remove existing placeholders
    document.querySelectorAll('.drop-placeholder').forEach(el => el.remove());

    const afterElement = getDragAfterElement(list, e.clientY);
    const placeholder = document.createElement('div');
    placeholder.className = 'drop-placeholder';

    if (afterElement) {
        list.insertBefore(placeholder, afterElement);
    } else {
        list.appendChild(placeholder);
    }
}

async function handleDrop(e) {
    e.preventDefault();
    const list = e.target.closest('.card-list');
    if (!list) return;

    list.classList.remove('drag-over');
    document.querySelectorAll('.drop-placeholder').forEach(el => el.remove());

    const cardId = e.dataTransfer.getData('text/plain');
    const column = list.closest('.kanban-column').dataset.column;

    // Calculate position
    const visibleCards = Array.from(list.querySelectorAll('.kanban-card:not(.dragging):not(.hidden)'));
    const afterElement = getDragAfterElement(list, e.clientY);
    let position;
    if (afterElement) {
        position = visibleCards.indexOf(afterElement);
        if (position < 0) position = visibleCards.length;
    } else {
        position = visibleCards.length;
    }

    await fetch(`${API.cards}/${cardId}/move`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ column, position })
    });

    await loadCards();
}

function getDragAfterElement(container, y) {
    const draggableElements = Array.from(
        container.querySelectorAll('.kanban-card:not(.dragging):not(.hidden)')
    );

    return draggableElements.reduce((closest, child) => {
        const box = child.getBoundingClientRect();
        const offset = y - box.top - box.height / 2;
        if (offset < 0 && offset > closest.offset) {
            return { offset, element: child };
        }
        return closest;
    }, { offset: Number.NEGATIVE_INFINITY }).element;
}

// ── Label Management ──
function openLabelManager() {
    renderLabelList();
    new bootstrap.Modal(document.getElementById('labelModal')).show();
}

function renderLabelList() {
    const container = document.getElementById('labelList');
    if (allLabels.length === 0) {
        container.innerHTML = '<p class="text-muted">No labels yet.</p>';
        return;
    }
    container.innerHTML = allLabels.map(l => `
        <div class="label-item" data-id="${l.id}">
            <div class="label-preview" style="background:${l.color}"></div>
            <input type="text" value="${escapeHtml(l.name)}" data-field="name" />
            <input type="color" value="${l.color}" data-field="color" style="width:36px;height:28px;padding:0;border:none;" />
            <button onclick="updateLabel('${l.id}')" title="Save">💾</button>
            <button onclick="deleteLabel('${l.id}')" title="Delete">🗑️</button>
        </div>
    `).join('');
}

async function createLabel() {
    const name = document.getElementById('newLabelName').value.trim();
    const color = document.getElementById('newLabelColor').value;
    if (!name) { alert('Label name is required'); return; }

    await fetch(API.labels, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name, color })
    });

    document.getElementById('newLabelName').value = '';
    await loadLabels();
    renderLabelList();
}

async function updateLabel(id) {
    const item = document.querySelector(`.label-item[data-id="${id}"]`);
    const name = item.querySelector('[data-field="name"]').value.trim();
    const color = item.querySelector('[data-field="color"]').value;
    if (!name) { alert('Label name is required'); return; }

    await fetch(`${API.labels}/${id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name, color })
    });

    await loadLabels();
    renderLabelList();
    renderCards();
}

async function deleteLabel(id) {
    if (!confirm('Delete this label? It will be removed from all cards.')) return;
    await fetch(`${API.labels}/${id}`, { method: 'DELETE' });
    await loadLabels();
    renderLabelList();
    await loadCards();
}

// ── Util ──
function renderMarkdown(text) {
    if (typeof marked !== 'undefined') {
        marked.setOptions({ breaks: true, gfm: true });
        return marked.parse(text);
    }
    return escapeHtml(text);
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}
