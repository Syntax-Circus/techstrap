import http from 'k6/http';

const FIRST = ['Ava', 'Ben', 'Chloe', 'Dev', 'Elena', 'Farid', 'Grace', 'Hugo', 'Iris', 'Jonas', 'Kira', 'Liam'];
const LAST = ['Archer', 'Bishop', 'Castillo', 'Dalton', 'Ellis', 'Fischer', 'Garcia', 'Hayes', 'Ito', 'Jensen', 'Khan', 'Lopez'];
const SUBJECTS = [
  'Cannot sign in after password reset',
  'Dark mode setting does not persist',
  'Invoice PDF is missing the VAT number',
  'Export to CSV times out on large projects',
  'Notification emails arrive twice',
  'How do I invite a teammate to my workspace',
  'Dashboard charts show yesterday data',
  'Mobile app crashes when opening settings',
  'Unable to change the billing contact',
  'Webhook deliveries are delayed',
  'Search returns no results for exact titles',
  'Two-factor codes are rejected',
  'Attachment upload fails above 5 MB',
  'Request to rename my organization',
  'API returns 404 for a project that exists',
];
const PARAGRAPHS = [
  'Since this morning I am unable to complete the action I use every day. The page loads but nothing happens when I press the button.',
  'I tried again in a private window and on a second computer and the behaviour is the same, so I do not think it is my browser.',
  'Our team of eight relies on this feature for our weekly planning, so a quick answer would be very much appreciated.',
  'I have attached nothing yet but can send screenshots and the exact time of each attempt if that helps your investigation.',
  'The problem started after the latest release notes were published. Before that everything worked without any trouble at all.',
  'Please let me know whether there is a workaround in the meantime, because we have a deadline at the end of the week.',
  'The error message says something went wrong and asks me to try again later, but trying again later has not helped so far.',
  'I checked the help articles about this topic and followed every step, but my account still behaves differently from the guide.',
  'This is not urgent for me personally, but several colleagues have reported the same thing so it may affect other customers too.',
  'Thank you for your help. I am happy to jump on a call or answer any questions you have about our configuration.',
];

export function ticket(n) {
  const i = Math.abs(n);
  const name = FIRST[i % FIRST.length] + ' ' + LAST[Math.floor(i / FIRST.length) % LAST.length];
  const count = 3 + (i % 6);
  const parts = [];
  for (let k = 0; k < count; k++) {
    parts.push(PARAGRAPHS[(i + k) % PARAGRAPHS.length]);
  }
  return {
    email: 'load+' + i + '@example.com',
    name: name,
    subject: SUBJECTS[i % SUBJECTS.length] + ' (load ' + i + ')',
    body: parts.join('\n\n'),
    externalUserRef: 'load-user-' + (i % 500),
    metadata: { source: 'k6' },
  };
}

export function attachment(n) {
  const line = 'k6 load attachment line for ticket ' + n + '\n';
  return http.file(line.repeat(Math.ceil(1024 / line.length)).substring(0, 1024), 'note-' + n + '.txt', 'text/plain');
}

export function idempotencyKey() {
  return 'ts-load-' + __VU + '-' + __ITER + '-' + Date.now();
}

// Builds a multipart/form-data body by hand: k6 only switches to multipart when a file is present, but the
// public endpoint accepts nothing else. `file` is an optional http.file() value sent under the field name `fileField`.
export function multipart(fields, file, fileField) {
  const boundary = '----tsload' + __VU + 'x' + __ITER + 'x' + Date.now();
  let body = '';
  Object.keys(fields).forEach(function (key) {
    body += '--' + boundary + '\r\nContent-Disposition: form-data; name="' + key + '"\r\n\r\n' + fields[key] + '\r\n';
  });
  if (file) {
    body += '--' + boundary + '\r\nContent-Disposition: form-data; name="' + fileField + '"; filename="' + file.filename + '"\r\nContent-Type: ' + file.content_type + '\r\n\r\n' + file.data + '\r\n';
  }
  body += '--' + boundary + '--\r\n';
  return { body: body, contentType: 'multipart/form-data; boundary=' + boundary };
}
